using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;

namespace BettingRoyale.Launcher.Core;

public sealed class FilePatchEngine(HttpClient httpClient)
{
    private const string WorkDirectoryName = ".bettingroyale-patch";

    public async Task<PatchPlan> AnalyzeAsync(
        string installRoot,
        ReleaseManifest remoteManifest,
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        remoteManifest.Validate();
        var localManifest = await LocalReleaseStore.ReadManifestAsync(installRoot, cancellationToken).ConfigureAwait(false);
        var managed = await LocalReleaseStore.ReadManagedFilesAsync(installRoot, cancellationToken).ConfigureAwait(false);
        var remotePaths = remoteManifest.Files
            .Select(file => SafePath.NormalizeRelative(file.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stale = managed.Where(path => !remotePaths.Contains(path)).ToArray();
        var invalid = new List<ReleaseFile>();
        long checkedBytes = 0;
        var totalBytes = remoteManifest.TotalSize;

        foreach (var file in remoteManifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PatchProgress("파일 검사", file.Path, checkedBytes, totalBytes, 0));
            var target = SafePath.ResolveUnderRoot(installRoot, file.Path);
            if (!await FileIntegrity.MatchesAsync(target, file, cancellationToken).ConfigureAwait(false) ||
                (file.Executable == true && !LauncherPlatform.HasExecutePermission(target)))
            {
                // 내용이 같아도 실행 권한을 잃은 실행 파일은 시작할 수 없으므로 다시 받는다(Unix 전용 판정).
                invalid.Add(file);
            }

            checkedBytes += file.Size;
        }

        var state = localManifest is null
            ? InstallState.NotInstalled
            : invalid.Count > 0 && string.Equals(localManifest.Version, remoteManifest.Version, StringComparison.Ordinal)
                ? InstallState.RepairRequired
                : !string.Equals(localManifest.Version, remoteManifest.Version, StringComparison.Ordinal) || stale.Length > 0
                    ? InstallState.UpdateAvailable
                    : invalid.Count > 0
                        ? InstallState.UpdateAvailable
                        : InstallState.Ready;

        return new PatchPlan(state, remoteManifest, localManifest, invalid, stale);
    }

    public async Task<PatchResult> ApplyAsync(
        Uri manifestUri,
        string installRoot,
        PatchPlan plan,
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        plan.RemoteManifest.Validate();
        Directory.CreateDirectory(installRoot);
        var workRoot = Path.Combine(installRoot, WorkDirectoryName);
        Directory.CreateDirectory(workRoot);
        await using var processLock = new FileStream(
            Path.Combine(workRoot, "operation.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        var operationRoot = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
        var stagingRoot = Path.Combine(operationRoot, "staging");
        var backupRoot = Path.Combine(operationRoot, "backup");
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(backupRoot);

        var stopwatch = Stopwatch.StartNew();
        long completedBytes = 0;
        var applied = new List<string>();
        var backedUp = new List<string>();
        var metadataFiles = new[] { LocalReleaseStore.ManifestFileName, LocalReleaseStore.ManagedFileName };
        var metadataOriginallyExisting = metadataFiles
            .Where(metadataFile => File.Exists(SafePath.ResolveUnderRoot(installRoot, metadataFile)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stage = "DOWNLOAD";
        var rollbackFailed = false;
        try
        {
            foreach (var file in plan.FilesToDownload)
            {
                stage = "DOWNLOAD";
                cancellationToken.ThrowIfCancellationRequested();
                var stagedPath = SafePath.ResolveUnderRoot(stagingRoot, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                var fileUri = ManifestClient.ResolveFileUri(manifestUri, file);
                using var request = new HttpRequestMessage(HttpMethod.Get, fileUri);
                request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var wire = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var encodings = response.Content.Headers.ContentEncoding;
                var isGzip = encodings.Count == 1 && encodings.Single().Equals("gzip", StringComparison.OrdinalIgnoreCase);
                if (encodings.Count > 0 &&
                    !isGzip && (encodings.Count != 1 || !encodings.Single().Equals("identity", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException($"지원하지 않는 다운로드 압축 형식입니다: {file.Path}");
                await using var decompressed = isGzip
                    ? new GZipStream(wire, CompressionMode.Decompress, leaveOpen: true)
                    : null;
                Stream input = decompressed ?? wire;
                await using var output = new FileStream(
                    stagedPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 128,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[1024 * 128];
                long fileBytes = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    // Enforce the uncompressed manifest limit before writing to disk.
                    if (read > file.Size - fileBytes)
                        throw new InvalidDataException($"다운로드한 파일이 manifest 크기를 초과했습니다: {file.Path}");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    fileBytes += read;
                    completedBytes += read;
                    var rate = stopwatch.Elapsed.TotalSeconds <= 0 ? 0 : completedBytes / stopwatch.Elapsed.TotalSeconds;
                    progress?.Report(new PatchProgress(
                        "다운로드",
                        file.Path,
                        completedBytes,
                        plan.DownloadBytes,
                        rate));
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Close();
                stage = "VERIFY";
                if (!await FileIntegrity.MatchesAsync(stagedPath, file, cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidDataException($"다운로드한 파일의 무결성 검사가 실패했습니다: {file.Path}");
                }

                // 교체 전에 권한을 정해 두면 설치 경로에는 처음부터 올바른 모드로 나타난다(Unix 전용, Windows 는 무시).
                LauncherPlatform.ApplyFileMode(stagedPath, file.Executable == true);
            }

            stage = "INSTALL";
            progress?.Report(new PatchProgress("파일 교체", string.Empty, completedBytes, plan.DownloadBytes, 0));
            foreach (var metadataFile in metadataFiles)
            {
                var target = SafePath.ResolveUnderRoot(installRoot, metadataFile);
                if (!File.Exists(target))
                {
                    continue;
                }

                var backup = SafePath.ResolveUnderRoot(backupRoot, metadataFile);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Move(target, backup);
                backedUp.Add(metadataFile);
            }

            foreach (var file in plan.FilesToDownload)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = SafePath.NormalizeRelative(file.Path);
                var target = SafePath.ResolveUnderRoot(installRoot, relative);
                var staged = SafePath.ResolveUnderRoot(stagingRoot, relative);
                var backup = SafePath.ResolveUnderRoot(backupRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Move(target, backup);
                    backedUp.Add(relative);
                }

                File.Move(staged, target);
                applied.Add(relative);
            }

            foreach (var relative in plan.StaleManagedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = SafePath.ResolveUnderRoot(installRoot, relative);
                if (!File.Exists(target))
                {
                    continue;
                }

                var backup = SafePath.ResolveUnderRoot(backupRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Move(target, backup);
                backedUp.Add(relative);
            }

            if (!OperatingSystem.IsWindows())
            {
                // 이번에 받지 않은 기존 파일도 manifest 의 executable 표시와 권한을 맞춘다.
                foreach (var file in plan.RemoteManifest.Files)
                {
                    var target = SafePath.ResolveUnderRoot(installRoot, file.Path);
                    if (File.Exists(target)) LauncherPlatform.ApplyFileMode(target, file.Executable == true);
                }
            }

            await LocalReleaseStore.WriteAsync(installRoot, plan.RemoteManifest, cancellationToken).ConfigureAwait(false);
            progress?.Report(new PatchProgress("완료", string.Empty, plan.DownloadBytes, plan.DownloadBytes, 0));
            return new PatchResult(plan.RemoteManifest.Version, completedBytes, plan.FilesToDownload.Count);
        }
        catch (Exception exception)
        {
            LauncherDiagnostics.At(exception, stage);
            try
            {
                foreach (var metadataFile in metadataFiles)
                {
                    var current = SafePath.ResolveUnderRoot(installRoot, metadataFile);
                    var backup = SafePath.ResolveUnderRoot(backupRoot, metadataFile);
                    var originalWasMoved = File.Exists(backup);
                    var currentWasNewlyWritten = originalWasMoved || !metadataOriginallyExisting.Contains(metadataFile);
                    if (currentWasNewlyWritten && File.Exists(current))
                    {
                        File.Delete(current);
                    }
                }

                foreach (var relative in applied.AsEnumerable().Reverse())
                {
                    var target = SafePath.ResolveUnderRoot(installRoot, relative);
                    if (File.Exists(target))
                    {
                        File.Delete(target);
                    }
                }

                foreach (var relative in backedUp.AsEnumerable().Reverse())
                {
                    var backup = SafePath.ResolveUnderRoot(backupRoot, relative);
                    var target = SafePath.ResolveUnderRoot(installRoot, relative);
                    if (!File.Exists(backup))
                    {
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Move(backup, target, true);
                }

            }
            catch (Exception rollbackException)
            {
                rollbackFailed = true;
                throw LauncherDiagnostics.At(new IOException("Patch rollback failed; backup retained.",
                    new AggregateException(exception, rollbackException)), "ROLLBACK");
            }
            throw;
        }
        finally
        {
            if (!rollbackFailed) TryDeleteDirectory(operationRoot);
        }
    }

    public static void ClearAbandonedWork(string installRoot)
    {
        var workRoot = Path.Combine(installRoot, WorkDirectoryName);
        if (!Directory.Exists(workRoot))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(workRoot))
        {
            var backup = Path.Combine(directory, "backup");
            if (Directory.Exists(backup) && Directory.EnumerateFiles(backup, "*", SearchOption.AllDirectories).Any())
                throw LauncherDiagnostics.At(new IOException("Unrecovered patch backup requires manual recovery."), "ROLLBACK");
            TryDeleteDirectory(directory);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (IOException)
        {
            // 다음 실행에서 다시 정리한다. 정상 설치물에는 영향이 없다.
        }
        catch (UnauthorizedAccessException)
        {
            // 잠긴 임시 파일은 다음 실행에서 다시 정리한다.
        }
    }
}

using System.Diagnostics;

namespace BettingRoyale.Launcher.Core;

/// <summary>macOS 런처 자체 갱신 판정 결과. <see cref="ChangedFiles"/> 가 비어 있으면 최신이다.</summary>
public sealed record LauncherSelfUpdatePlan(ReleaseManifest Manifest, string BundleName, IReadOnlyList<ReleaseFile> ChangedFiles)
{
    public bool UpdateAvailable => ChangedFiles.Count > 0;
    public long DownloadBytes => ChangedFiles.Sum(file => file.Size);
}

/// <summary>
/// Bootstrap 이 없는 macOS 런처의 자체 갱신(S15P21A606-312). Windows 에서는 Bootstrap 이 같은 일을 한다.
/// </summary>
/// <remarks>
/// <para>
/// <c>launcher/macos/manifest.json</c> 의 파일 경로는 <c>&lt;번들 이름&gt;.app/...</c> 이다. 실행 중인 <c>.app</c> 을
/// manifest 와 해시로 비교하고, 바뀐 파일만 받아 staging 에 새 번들을 완성한다(바뀌지 않은 파일은 현재 번들에서 복사).
/// 실행 중인 번들을 스스로 덮어쓰지 않는다 — 런처가 끝난 뒤 작은 셸 스크립트가 번들을 통째로 바꾸고 다시 연다.
/// </para>
/// <para>
/// 서명된 <c>.app</c> 안에는 파일을 추가하지 않는다(서명 봉인이 깨진다). 설치 기록은 staging 루트에만 남는다.
/// </para>
/// </remarks>
public sealed class LauncherSelfUpdater(HttpClient httpClient)
{
    public const string Product = "BettingRoyaleLauncher";

    /// <summary>
    /// 실행 파일 폴더(<c>AppContext.BaseDirectory</c>)가 <c>X.app/Contents/MacOS</c> 이면 <c>X.app</c> 경로를 돌려준다.
    /// 번들 밖(개발 중 <c>dotnet run</c>)이면 null 이다.
    /// </summary>
    public static string? ResolveRunningBundle(string baseDirectory)
    {
        var macOs = new DirectoryInfo(Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var contents = macOs.Parent;
        var bundle = contents?.Parent;
        return macOs.Name == "MacOS" && contents?.Name == "Contents" &&
               bundle is not null && bundle.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            ? bundle.FullName
            : null;
    }

    /// <summary>manifest 를 받아 실행 중인 번들과 비교한다. 실패 단계는 MANIFEST 또는 CHECK 다.</summary>
    public async Task<LauncherSelfUpdatePlan> CheckAsync(Uri manifestUri, string bundlePath, CancellationToken cancellationToken = default)
    {
        var manifest = await new ManifestClient(httpClient)
            .GetAsync(manifestUri, Product, ReleasePlatform.MacOSArm64, cancellationToken).ConfigureAwait(false);
        try
        {
            var bundleName = ValidateBundleLayout(manifest, bundlePath);
            var parent = Path.GetDirectoryName(Path.GetFullPath(bundlePath))!;
            var changed = new List<ReleaseFile>();
            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await FileIntegrity.MatchesAsync(SafePath.ResolveUnderRoot(parent, file.Path), file, cancellationToken).ConfigureAwait(false))
                    changed.Add(file);
            }

            return new LauncherSelfUpdatePlan(manifest, bundleName, changed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw LauncherDiagnostics.At(exception, "CHECK");
        }
    }

    /// <summary>
    /// 번들을 교체할 수 있는지 먼저 확인한다(받기 전에 실패시켜 헛다운로드를 막는다). 번들의 부모 폴더에 쓸 수 있어야
    /// 이름을 바꿀 수 있다. App Translocation(다운로드 폴더에서 바로 실행)이나 관리자 소유 폴더면
    /// <see cref="UnauthorizedAccessException"/>(INSTALL 단계 → <c>BRL-B-INSTALL-ACCESS</c>)이다.
    /// </summary>
    public static void EnsureCanReplace(string bundlePath)
    {
        var fullPath = Path.GetFullPath(bundlePath);
        if (fullPath.Contains("/AppTranslocation/", StringComparison.Ordinal))
        {
            throw LauncherDiagnostics.At(new UnauthorizedAccessException(
                "The launcher runs from a translocated read-only location."), "INSTALL");
        }

        var parent = Path.GetDirectoryName(fullPath)!;
        var probe = Path.Combine(parent, $".bettingroyale-write-probe-{Guid.NewGuid():N}");
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            File.Delete(probe);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw LauncherDiagnostics.At(new UnauthorizedAccessException(
                $"Cannot write next to the launcher bundle: {parent}", exception), "INSTALL");
        }
    }

    /// <summary>
    /// staging 루트에 새 번들을 완성하고 그 경로(<c>&lt;staging&gt;/&lt;번들 이름&gt;</c>)를 돌려준다.
    /// 바뀌지 않은 파일은 현재 번들에서 복사하고(APFS 는 복제라 빠르다) 나머지만 받는다. 실패 단계는 DOWNLOAD/VERIFY/INSTALL 이다.
    /// </summary>
    public async Task<string> StageAsync(Uri manifestUri, string bundlePath, LauncherSelfUpdatePlan plan, string stagingRoot,
        IProgress<PatchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var stage = "INSTALL";
        try
        {
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, true);
            Directory.CreateDirectory(stagingRoot);
            var parent = Path.GetDirectoryName(Path.GetFullPath(bundlePath))!;
            var changed = plan.ChangedFiles.Select(file => SafePath.NormalizeRelative(file.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in plan.Manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = SafePath.NormalizeRelative(file.Path);
                if (changed.Contains(relative)) continue;
                var destination = SafePath.ResolveUnderRoot(stagingRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(SafePath.ResolveUnderRoot(parent, relative), destination);
                LauncherPlatform.ApplyFileMode(destination, file.Executable == true);
            }

            stage = "CHECK";
            var engine = new FilePatchEngine(httpClient);
            var patch = await engine.AnalyzeAsync(stagingRoot, plan.Manifest, null, cancellationToken).ConfigureAwait(false);
            stage = "DOWNLOAD";
            await engine.ApplyAsync(manifestUri, stagingRoot, patch, progress, cancellationToken).ConfigureAwait(false);
            return SafePath.ResolveUnderRoot(stagingRoot, plan.BundleName);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 엔진이 이미 DOWNLOAD/VERIFY/INSTALL 로 표시했으면 그 값이 남는다.
            throw LauncherDiagnostics.At(exception, stage);
        }
    }

    /// <summary>번들 교체 스크립트를 쓴다. 인자: 런처 PID, 대상 번들, staging 번들, 로그 파일, 다시 여는 명령.</summary>
    public static string WriteSwapScript(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "self-update.sh");
        File.WriteAllText(path, SwapScript.Replace("\r\n", "\n", StringComparison.Ordinal));
        LauncherPlatform.ApplyFileMode(path, executable: true);
        return path;
    }

    /// <summary>
    /// 교체 스크립트를 런처와 분리해 시작한다. 호출한 뒤 런처는 곧바로 종료해야 한다 — 스크립트는 PID 가 사라질 때까지 기다린다.
    /// </summary>
    public static void StartSwap(string scriptPath, int launcherPid, string bundlePath, string stagedBundlePath, string logPath,
        string relaunchCommand = "/usr/bin/open")
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("번들 교체는 macOS 런처 전용입니다.");
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        foreach (var argument in new[]
                 {
                     scriptPath, launcherPid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     Path.GetFullPath(bundlePath), Path.GetFullPath(stagedBundlePath), logPath, relaunchCommand,
                 })
            start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("런처 교체 스크립트를 시작하지 못했습니다.");
        }
        catch (Exception exception)
        {
            throw LauncherDiagnostics.At(exception, "LAUNCH");
        }
    }

    private static string ValidateBundleLayout(ReleaseManifest manifest, string bundlePath)
    {
        var executable = SafePath.NormalizeRelative(manifest.Executable);
        var bundleName = executable.Split('/')[0];
        if (!bundleName.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ||
            !executable.StartsWith(bundleName + "/Contents/MacOS/", StringComparison.Ordinal))
            throw new InvalidDataException($"런처 manifest 실행 파일은 <번들>.app/Contents/MacOS/ 아래여야 합니다: {manifest.Executable}");
        if (manifest.Files.Any(file => !SafePath.NormalizeRelative(file.Path).StartsWith(bundleName + "/", StringComparison.Ordinal)))
            throw new InvalidDataException($"런처 manifest 의 모든 파일은 {bundleName} 안에 있어야 합니다.");
        // Production 번들이 Preview manifest 로(또는 그 반대로) 바뀌는 것을 막는다 — 번들 이름·ID 가 환경마다 다르다.
        if (!string.Equals(bundleName, Path.GetFileName(Path.GetFullPath(bundlePath)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"실행 중인 번들과 manifest 번들 이름이 다릅니다: {bundleName}");
        return bundleName;
    }

    // POSIX sh. 런처가 끝날 때까지(최대 60초) 기다린 뒤 이전 번들을 옆으로 옮기고 새 번들을 넣는다. 실패하면 이전 번들을 되돌린다.
    private const string SwapScript = """
        #!/bin/sh
        # BettingRoyale macOS launcher self-update (S15P21A606-312). Generated by LauncherSelfUpdater.
        pid="$1"; target="$2"; staged="$3"; log="$4"; relaunch="$5"
        note() { printf '%s [%s] %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$1" "$2" >> "$log" 2>/dev/null; }
        waited=0
        while kill -0 "$pid" 2>/dev/null; do
          waited=$((waited + 1))
          if [ "$waited" -gt 600 ]; then note ERROR "BRL-B-INSTALL-STATE launcher did not exit"; exit 1; fi
          sleep 0.1
        done
        backup="$target.previous-$$"
        if ! mv "$target" "$backup" 2>>"$log"; then
          note ERROR "BRL-B-INSTALL-ACCESS cannot move current bundle: $target"
          "$relaunch" "$target"; exit 1
        fi
        if ! mv "$staged" "$target" 2>>"$log"; then
          rm -rf "$target"; mv "$backup" "$target"
          note ERROR "BRL-B-INSTALL-IO cannot place new bundle; restored previous bundle"
          "$relaunch" "$target"; exit 1
        fi
        rm -rf "$backup"
        note INFO "launcher bundle replaced: $target"
        "$relaunch" "$target"
        """;
}

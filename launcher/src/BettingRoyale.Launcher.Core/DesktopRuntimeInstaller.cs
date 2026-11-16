using System.Diagnostics;
using System.Net.Http.Headers;

namespace BettingRoyale.Launcher.Core;

/// <summary>
/// framework-dependent Launcher 가 요구하는 .NET Desktop Runtime 을 확인하고, 없을 때만 설치한다 (D-REL-RW5QWY).
/// </summary>
/// <remarks>
/// <para>
/// 설치 파일은 우리 서버의 <c>launcher/runtime/</c> 에서 받는다. manifest 의 크기·SHA-256 을 통과한 파일만 실행하며,
/// Microsoft 원본 URL 을 사용자 PC 가 직접 열지 않는다. 설치기 자체가 UAC 승격을 요청한다.
/// </para>
/// <para>
/// 검사는 파일 시스템으로 한다 — <c>%ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App\&lt;버전&gt;</c>.
/// <c>dotnet --list-runtimes</c> 는 dotnet 호스트가 없는 PC 에서 그 자체가 실패한다.
/// </para>
/// <para>
/// <b>Windows 전용이다.</b> macOS 런처는 self-contained 로 배포하고 Bootstrap 이 없으므로 이 클래스를 쓰지 않는다.
/// <c>runtime</c> 필드는 <c>windows-x64</c> manifest 에서만 허용된다(<see cref="ReleaseManifest.Validate"/>, S15P21A606-312).
/// </para>
/// </remarks>
public sealed class DesktopRuntimeInstaller(HttpClient httpClient, FileLauncherLog? log = null)
{
    /// <summary>설치기가 성공으로 보는 종료 코드. 3010 은 재부팅 필요, 1638 은 같은/상위 버전이 이미 있음.</summary>
    public static readonly int[] AcceptedExitCodes = [0, 1638, 3010];

    public const string InstallerArguments = "/install /quiet /norestart";

    /// <summary>테스트가 가짜 dotnet 루트를 넣을 수 있게 열어 둔다. 기본은 실제 설치 위치다.</summary>
    public IReadOnlyList<string> SharedFrameworkRoots { get; init; } = DefaultSharedFrameworkRoots();

    /// <summary>설치기 실행. 테스트는 실행하지 않고 종료 코드만 흉내 낸다.</summary>
    public Func<string, CancellationToken, Task<int>> RunInstaller { get; init; } = RunInstallerProcessAsync;

    public static IReadOnlyList<string> DefaultSharedFrameworkRoots()
    {
        var roots = new List<string>();
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot)) roots.Add(Path.Combine(dotnetRoot, "shared"));
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles)) roots.Add(Path.Combine(programFiles, "dotnet", "shared"));
        return roots;
    }

    /// <summary>같은 major 에서 요구 버전 이상이 하나라도 있으면 충족이다. 다른 major 는 롤포워드 대상이 아니다.</summary>
    public static bool IsSatisfied(IEnumerable<string> sharedFrameworkRoots, RuntimeRequirement requirement)
    {
        var required = requirement.ParsedVersion;
        foreach (var root in sharedFrameworkRoots)
        {
            var frameworkRoot = Path.Combine(root, requirement.Name);
            if (!Directory.Exists(frameworkRoot)) continue;
            foreach (var directory in Directory.EnumerateDirectories(frameworkRoot))
            {
                if (!Version.TryParse(Path.GetFileName(directory), out var installed)) continue;
                if (installed.Major == required.Major && installed >= required &&
                    File.Exists(Path.Combine(directory, requirement.Name + ".deps.json")))
                    return true;
            }
        }

        return false;
    }

    public bool IsSatisfied(RuntimeRequirement requirement) => IsSatisfied(SharedFrameworkRoots, requirement);

    /// <summary>
    /// 런타임이 없으면 받아서 설치한다. 설치했으면 true, 이미 있거나 요구가 없으면 false.
    /// 실패는 <c>RUNTIME</c> 단계로 표시된다.
    /// </summary>
    public async Task<bool> EnsureAsync(Uri manifestUri, RuntimeRequirement? requirement, string cacheRoot,
        IProgress<PatchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (requirement is null) return false;
        try
        {
            requirement.Validate();
            if (IsSatisfied(requirement))
            {
                if (log is not null) await log.InfoAsync($"{requirement.Name} {requirement.Version} 이상이 이미 설치되어 있습니다.");
                return false;
            }

            var file = requirement.AsReleaseFile();
            Directory.CreateDirectory(cacheRoot);
            var installerPath = SafePath.ResolveUnderRoot(cacheRoot, file.Path);
            if (!await FileIntegrity.MatchesAsync(installerPath, file, cancellationToken).ConfigureAwait(false))
            {
                await DownloadAsync(manifestUri, file, installerPath, progress, cancellationToken).ConfigureAwait(false);
                if (!await FileIntegrity.MatchesAsync(installerPath, file, cancellationToken).ConfigureAwait(false))
                {
                    File.Delete(installerPath);
                    throw new InvalidDataException($"런타임 설치 파일의 무결성 검사가 실패했습니다: {file.Path}");
                }
            }

            progress?.Report(new PatchProgress("실행 환경 설치", file.Path, file.Size, file.Size, 0));
            if (log is not null) await log.InfoAsync($"{requirement.Name} {requirement.Version} 설치 시작: {installerPath}");
            var exitCode = await RunInstaller(installerPath, cancellationToken).ConfigureAwait(false);
            if (!AcceptedExitCodes.Contains(exitCode))
                throw new InvalidOperationException($"런타임 설치기가 실패했습니다 (종료 코드 {exitCode}).");
            if (!IsSatisfied(requirement))
                throw new InvalidOperationException($"설치기는 끝났지만 {requirement.Name} {requirement.Version} 을 찾을 수 없습니다.");
            if (log is not null) await log.InfoAsync($"{requirement.Name} {requirement.Version} 설치 완료 (종료 코드 {exitCode}).");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            throw LauncherDiagnostics.At(exception, "RUNTIME");
        }
    }

    private async Task DownloadAsync(Uri manifestUri, ReleaseFile file, string destination,
        IProgress<PatchProgress>? progress, CancellationToken cancellationToken)
    {
        var temporary = destination + ".partial";
        File.Delete(temporary);
        using var request = new HttpRequestMessage(HttpMethod.Get, ManifestClient.ResolveFileUri(manifestUri, file, "runtime"));
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var wire = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, FileOptions.Asynchronous))
        {
            var buffer = new byte[1024 * 128];
            long copied = 0;
            var stopwatch = Stopwatch.StartNew();
            int read;
            while ((read = await wire.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (read > file.Size - copied)
                    throw new InvalidDataException($"런타임 설치 파일이 manifest 크기를 초과했습니다: {file.Path}");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                copied += read;
                var rate = stopwatch.Elapsed.TotalSeconds <= 0 ? 0 : copied / stopwatch.Elapsed.TotalSeconds;
                progress?.Report(new PatchProgress("실행 환경 다운로드", file.Path, copied, file.Size, rate));
            }
        }

        File.Move(temporary, destination, overwrite: true);
    }

    private static async Task<int> RunInstallerProcessAsync(string installerPath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("런타임 설치는 Windows 에서만 지원합니다.");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = InstallerArguments,
            UseShellExecute = true, // 설치기가 스스로 UAC 승격을 요청하려면 ShellExecute 가 필요하다.
        }) ?? throw new InvalidOperationException("런타임 설치기를 시작하지 못했습니다.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }
}

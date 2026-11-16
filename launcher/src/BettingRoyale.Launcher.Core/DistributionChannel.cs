using System.Diagnostics;

namespace BettingRoyale.Launcher.Core;

public sealed record DistributionSnapshot(
    InstallState State,
    string ChannelName,
    bool ServerOnline,
    string Version,
    IReadOnlyList<string> PatchNotes,
    long DownloadBytes,
    bool IsGameRunning,
    string StatusMessage);

public interface IDistributionChannel
{
    string Name { get; }
    string InstallRoot { get; }
    bool SupportsManagedUpdates { get; }
    Task<DistributionSnapshot> RefreshAsync(
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default);
    Task<PatchResult> ApplyAsync(
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default);
    Task<PatchResult> RepairAsync(
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default);
    Task LaunchAsync(CancellationToken cancellationToken = default);
}

public sealed class DirectDistributionChannel : IDistributionChannel
{
    private readonly Uri manifestUri;
    private readonly ManifestClient manifestClient;
    private readonly FilePatchEngine patchEngine;
    private readonly FileLauncherLog log;
    private PatchPlan? latestPlan;

    public DirectDistributionChannel(
        string installRoot,
        Uri manifestUri,
        HttpClient httpClient,
        FileLauncherLog log)
    {
        InstallRoot = Path.GetFullPath(installRoot);
        this.manifestUri = manifestUri;
        this.log = log;
        manifestClient = new ManifestClient(httpClient);
        patchEngine = new FilePatchEngine(httpClient);
    }

    public string Name => "Direct";
    public string InstallRoot { get; }
    public bool SupportsManagedUpdates => true;

    public async Task<DistributionSnapshot> RefreshAsync(
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            FilePatchEngine.ClearAbandonedWork(InstallRoot);
            var manifest = await manifestClient.GetAsync(
                manifestUri,
                "BettingRoyale",
                cancellationToken).ConfigureAwait(false);
            latestPlan = await patchEngine.AnalyzeAsync(
                InstallRoot,
                manifest,
                progress,
                cancellationToken).ConfigureAwait(false);
            var message = latestPlan.State switch
            {
                InstallState.NotInstalled => "게임 설치가 필요합니다.",
                InstallState.UpdateAvailable => "새 게임 버전을 받을 수 있습니다.",
                InstallState.RepairRequired => "손상되거나 누락된 파일이 발견됐습니다.",
                _ => "최신 버전입니다.",
            };
            return new DistributionSnapshot(
                latestPlan.State,
                Name,
                true,
                manifest.Version,
                manifest.PatchNotes,
                latestPlan.DownloadBytes,
                IsGameRunning(manifest.Executable),
                message);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested &&
            exception is HttpRequestException or TaskCanceledException)
        {
            var failure = await LauncherDiagnostics.ReportAsync(exception, "L", "MANIFEST", log).ConfigureAwait(false);
            var local = await LocalReleaseStore.ReadManifestAsync(InstallRoot, cancellationToken).ConfigureAwait(false);
            if (local is not null)
            {
                latestPlan = await patchEngine.AnalyzeAsync(
                    InstallRoot,
                    local,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                var canRunOffline = latestPlan.State == InstallState.Ready;
                return new DistributionSnapshot(
                    latestPlan.State,
                    Name,
                    false,
                    local.Version,
                    local.PatchNotes,
                    latestPlan.DownloadBytes,
                    canRunOffline && IsGameRunning(local.Executable),
                    (canRunOffline
                        ? "서버에 연결할 수 없어 검증된 설치 버전으로 실행합니다."
                        : "설치 파일이 손상됐고 서버에 연결할 수 없어 복구가 필요합니다.") + "\n" + failure.Display);
            }

            throw;
        }
    }

    public async Task<PatchResult> ApplyAsync(
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var plan = latestPlan ?? throw new InvalidOperationException("먼저 버전 확인을 실행해야 합니다.");
        EnsureNotRunning(plan.RemoteManifest.Executable);
        var result = await patchEngine.ApplyAsync(
            manifestUri,
            InstallRoot,
            plan,
            progress,
            cancellationToken).ConfigureAwait(false);
        await log.InfoAsync($"게임 {result.Version} 적용 완료, 파일 {result.UpdatedFileCount}개").ConfigureAwait(false);
        latestPlan = await patchEngine.AnalyzeAsync(InstallRoot, plan.RemoteManifest, null, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<PatchResult> RepairAsync(
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var manifest = latestPlan?.RemoteManifest
            ?? await manifestClient.GetAsync(
                manifestUri,
                "BettingRoyale",
                cancellationToken).ConfigureAwait(false);
        EnsureNotRunning(manifest.Executable);
        var repairPlan = await patchEngine.AnalyzeAsync(InstallRoot, manifest, progress, cancellationToken).ConfigureAwait(false);
        return await patchEngine.ApplyAsync(
            manifestUri,
            InstallRoot,
            repairPlan,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task LaunchAsync(CancellationToken cancellationToken = default)
    {
        var manifest = latestPlan?.RemoteManifest
            ?? await LocalReleaseStore.ReadManifestAsync(InstallRoot, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("설치된 게임이 없습니다.");
        var executable = SafePath.ResolveUnderRoot(InstallRoot, manifest.Executable);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("게임 실행 파일을 찾을 수 없습니다.", executable);
        }

        if (IsGameRunning(manifest.Executable))
        {
            throw new InvalidOperationException("게임이 이미 실행 중입니다.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        LauncherPlatform.StartGame(executable, InstallRoot);
        await log.InfoAsync($"게임 실행: {manifest.Version}").ConfigureAwait(false);
    }

    private bool IsGameRunning(string executable) =>
        LauncherPlatform.IsRunning(SafePath.ResolveUnderRoot(InstallRoot, executable));

    private void EnsureNotRunning(string executable)
    {
        if (IsGameRunning(executable))
        {
            throw new InvalidOperationException("게임 실행 중에는 업데이트하거나 복구할 수 없습니다.");
        }
    }
}

public sealed class SteamDistributionChannel : IDistributionChannel
{
    private readonly string appId;

    public SteamDistributionChannel(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId) || appId.Any(ch => !char.IsAsciiDigit(ch)))
        {
            throw new InvalidDataException("Steam App ID가 설정되지 않았습니다.");
        }

        this.appId = appId;
    }

    public string Name => "Steam";
    public string InstallRoot => string.Empty;
    public bool SupportsManagedUpdates => false;

    public Task<DistributionSnapshot> RefreshAsync(
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new DistributionSnapshot(
            InstallState.Ready,
            Name,
            true,
            "Steam",
            ["설치와 업데이트는 Steam이 관리합니다."],
            0,
            false,
            "Steam에서 실행할 준비가 됐습니다."));

    public Task<PatchResult> ApplyAsync(
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Steam 채널의 설치와 업데이트는 Steam 클라이언트가 관리합니다.");

    public Task<PatchResult> RepairAsync(
        IProgress<PatchProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Steam의 파일 무결성 검사 기능을 사용하세요.");

    public Task LaunchAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo
        {
            FileName = $"steam://run/{appId}",
            UseShellExecute = true,
        });
        return Task.CompletedTask;
    }
}

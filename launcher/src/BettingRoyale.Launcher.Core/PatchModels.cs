namespace BettingRoyale.Launcher.Core;

public enum InstallState
{
    NotInstalled,
    UpdateAvailable,
    RepairRequired,
    Ready,
}

public sealed record PatchPlan(
    InstallState State,
    ReleaseManifest RemoteManifest,
    ReleaseManifest? LocalManifest,
    IReadOnlyList<ReleaseFile> FilesToDownload,
    IReadOnlyList<string> StaleManagedFiles)
{
    public long DownloadBytes => FilesToDownload.Sum(file => file.Size);
}

public sealed record PatchProgress(
    string Phase,
    string CurrentFile,
    long CompletedBytes,
    long TotalBytes,
    double BytesPerSecond)
{
    public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)CompletedBytes / TotalBytes, 0, 1);
}

public sealed record PatchResult(string Version, long DownloadedBytes, int UpdatedFileCount);

internal sealed record ManagedFileState
{
    public IReadOnlyList<string> Files { get; init; } = [];
}

namespace BettingRoyale.Launcher.Core;

public sealed record BootstrapRelease(string ExecutablePath, string Version, LauncherFailure? UpdateFailure = null,
    RuntimeRequirement? Runtime = null);

/// <summary>UI-independent Bootstrap update and verified offline fallback.</summary>
public sealed class BootstrapInstaller(HttpClient httpClient, FileLauncherLog log)
{
    public async Task<BootstrapRelease> PrepareAsync(Uri manifestUri, string installRoot,
        IProgress<PatchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var engine = new FilePatchEngine(httpClient);
        try
        {
            FilePatchEngine.ClearAbandonedWork(installRoot);
            var manifest = await new ManifestClient(httpClient).GetAsync(manifestUri, "BettingRoyaleLauncher", cancellationToken);
            var plan = await engine.AnalyzeAsync(installRoot, manifest, progress, cancellationToken);
            if (plan.State != InstallState.Ready)
                await engine.ApplyAsync(manifestUri, installRoot, plan, progress, cancellationToken);
            await log.InfoAsync($"런처 {manifest.Version} 준비 완료");
            return new BootstrapRelease(SafePath.ResolveUnderRoot(installRoot, manifest.Executable), manifest.Version, Runtime: manifest.Runtime);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            var failure = await LauncherDiagnostics.ReportAsync(exception, "B", "CHECK", log);
            var local = await LocalReleaseStore.ReadManifestAsync(installRoot, cancellationToken);
            // Never start a corrupted or partially rolled-back installation as an offline fallback.
            if (local is not null && local.Product == "BettingRoyaleLauncher" &&
                (await engine.AnalyzeAsync(installRoot, local, progress, cancellationToken)).State == InstallState.Ready)
                return new BootstrapRelease(SafePath.ResolveUnderRoot(installRoot, local.Executable), local.Version, failure, local.Runtime);
            throw;
        }
    }
}

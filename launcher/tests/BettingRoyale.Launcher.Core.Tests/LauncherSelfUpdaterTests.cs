using System.Diagnostics;
using System.Text.Json;
using BettingRoyale.Launcher.Core;
using static BettingRoyale.Launcher.Core.Tests.PlatformManifestTests;

namespace BettingRoyale.Launcher.Core.Tests;

/// <summary>macOS 런처 자체 갱신(S15P21A606-312). 판정·staging 은 어느 OS 에서나, 번들 교체 스크립트는 Unix 에서 검사한다.</summary>
public sealed class LauncherSelfUpdaterTests : IDisposable
{
    private const string Bundle = "BettingRoyale Launcher.app";
    private const string ExecutablePath = Bundle + "/Contents/MacOS/BettingRoyaleLauncher";
    private const string DllPath = Bundle + "/Contents/MacOS/BettingRoyaleLauncher.dll";
    private const string PlistPath = Bundle + "/Contents/Info.plist";

    private readonly string root = Path.Combine(Path.GetTempPath(), "BettingRoyaleSelfUpdateTests", Guid.NewGuid().ToString("N"));
    private readonly Uri manifestUri = new("https://download.test/launcher/macos/manifest.json");

    [Fact]
    public void RunningBundleIsResolvedOnlyInsideAnAppBundle()
    {
        var macOs = Path.Combine(root, Bundle, "Contents", "MacOS") + Path.DirectorySeparatorChar;
        Assert.Equal(Path.Combine(root, Bundle), LauncherSelfUpdater.ResolveRunningBundle(macOs));
        Assert.Null(LauncherSelfUpdater.ResolveRunningBundle(Path.Combine(root, "bin", "Debug", "net8.0")));
    }

    [Fact]
    public async Task UpToDateBundleNeedsNoUpdate()
    {
        var (manifest, server) = Release("v1", "exe-v1", "dll-v1", "plist-v1");
        var bundle = InstallBundle("exe-v1", "dll-v1", "plist-v1");
        using var http = new HttpClient(new StaticHandler(server));

        var plan = await new LauncherSelfUpdater(http).CheckAsync(manifestUri, bundle);

        Assert.False(plan.UpdateAvailable);
        Assert.Equal("v1", plan.Manifest.Version);
        Assert.Equal(Bundle, plan.BundleName);
    }

    [Fact]
    public async Task StagesNewBundleDownloadingOnlyChangedFiles()
    {
        var (manifest, server) = Release("v2", "exe-v2", "dll-v1", "plist-v2");
        var bundle = InstallBundle("exe-v1", "dll-v1", "plist-v1");
        System.IO.File.WriteAllText(Path.Combine(bundle, "Contents", "stale-from-old-version.txt"), "old");
        var handler = new StaticHandler(server);
        using var http = new HttpClient(handler);
        var updater = new LauncherSelfUpdater(http);

        var plan = await updater.CheckAsync(manifestUri, bundle);
        Assert.True(plan.UpdateAvailable);
        Assert.Equal(new[] { PlistPath, ExecutablePath }, plan.ChangedFiles.Select(file => file.Path).Order(StringComparer.Ordinal));

        handler.Requests.Clear();
        var staged = await updater.StageAsync(manifestUri, bundle, plan, Path.Combine(root, "staging"));

        Assert.Equal(Path.Combine(root, "staging", Bundle), staged);
        Assert.Equal(2, handler.Requests.Count); // 바뀐 두 파일만. 같은 DLL 은 현재 번들에서 복사한다.
        Assert.Equal("exe-v2", System.IO.File.ReadAllText(Path.Combine(staged, "Contents", "MacOS", "BettingRoyaleLauncher")));
        Assert.Equal("dll-v1", System.IO.File.ReadAllText(Path.Combine(staged, "Contents", "MacOS", "BettingRoyaleLauncher.dll")));
        Assert.False(System.IO.File.Exists(Path.Combine(staged, "Contents", "stale-from-old-version.txt")));
        // 설치 기록은 서명 봉인을 깨지 않도록 번들 밖(staging 루트)에만 둔다.
        Assert.False(Directory.EnumerateFiles(staged, ".bettingroyale-*", SearchOption.AllDirectories).Any());
        if (!OperatingSystem.IsWindows())
            Assert.Equal(LauncherPlatform.ExecutableMode, System.IO.File.GetUnixFileMode(Path.Combine(staged, "Contents", "MacOS", "BettingRoyaleLauncher")));
        // 실행 중인 번들은 그대로다 — 교체는 런처 종료 뒤 스크립트가 한다.
        Assert.Equal("exe-v1", System.IO.File.ReadAllText(Path.Combine(bundle, "Contents", "MacOS", "BettingRoyaleLauncher")));
    }

    [Fact]
    public async Task RejectsManifestForAnotherBundleName()
    {
        var (_, server) = Release("v2", "exe-v2", "dll-v1", "plist-v2");
        var bundle = InstallBundle("exe-v1", "dll-v1", "plist-v1");
        var preview = Path.Combine(root, "BettingRoyale Launcher Preview.app");
        Directory.Move(bundle, preview);
        using var http = new HttpClient(new StaticHandler(server));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new LauncherSelfUpdater(http).CheckAsync(manifestUri, preview));
        Assert.Equal("BRL-B-CHECK-FORMAT", LauncherDiagnostics.Describe(error, "B", "CHECK").Code);
    }

    [Fact]
    public async Task RejectsWindowsLauncherManifest()
    {
        var windows = new ReleaseManifest
        {
            Product = LauncherSelfUpdater.Product, Version = "v1", Executable = "BettingRoyaleLauncher.exe",
            Files = [PlatformManifestTests.File("BettingRoyaleLauncher.exe", Bytes("x"), null)],
        };
        using var http = new HttpClient(new StaticHandler(new Dictionary<string, byte[]>
        {
            [manifestUri.AbsoluteUri] = JsonSerializer.SerializeToUtf8Bytes(windows, LauncherJson.Options),
        }));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new LauncherSelfUpdater(http).CheckAsync(manifestUri, InstallBundle("a", "b", "c")));
        Assert.Equal("BRL-B-MANIFEST-FORMAT", LauncherDiagnostics.Describe(error, "B", "CHECK").Code);
    }

    [Fact]
    public void TranslocatedBundleCannotBeReplaced()
    {
        var error = Assert.Throws<UnauthorizedAccessException>(() =>
            LauncherSelfUpdater.EnsureCanReplace("/private/var/folders/x/AppTranslocation/ABC/d/" + Bundle));
        Assert.Equal("BRL-B-INSTALL-ACCESS", LauncherDiagnostics.Describe(error, "B", "CHECK").Code);
    }

    [UnixFact, System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void ReadOnlyBundleFolderIsReportedAsAccessFailure()
    {
        if (Environment.UserName == "root") return; // CI 컨테이너의 root 는 권한 비트를 무시한다.
        var bundle = InstallBundle("a", "b", "c");
        System.IO.File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var error = Assert.Throws<UnauthorizedAccessException>(() => LauncherSelfUpdater.EnsureCanReplace(bundle));
            Assert.Equal("BRL-B-INSTALL-ACCESS", LauncherDiagnostics.Describe(error, "B", "CHECK").Code);
        }
        finally
        {
            System.IO.File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        LauncherSelfUpdater.EnsureCanReplace(bundle);
    }

    [UnixFact, System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task SwapScriptReplacesBundleAfterLauncherExits()
    {
        var bundle = InstallBundle("exe-v1", "dll-v1", "plist-v1");
        var staged = Path.Combine(root, "staging", Bundle);
        Directory.CreateDirectory(Path.Combine(staged, "Contents", "MacOS"));
        System.IO.File.WriteAllText(Path.Combine(staged, "Contents", "MacOS", "BettingRoyaleLauncher"), "exe-v2");
        var script = LauncherSelfUpdater.WriteSwapScript(Path.Combine(root, "scripts"));
        var log = Path.Combine(root, "self-update.log");
        using var exited = Process.Start(new ProcessStartInfo("/bin/sh", "-c true") { UseShellExecute = false })!;
        await exited.WaitForExitAsync();

        using var swap = Process.Start(new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            ArgumentList = { script, exited.Id.ToString(), bundle, staged, log, "true" },
        })!;
        await swap.WaitForExitAsync();

        Assert.Equal(0, swap.ExitCode);
        Assert.Equal("exe-v2", System.IO.File.ReadAllText(Path.Combine(bundle, "Contents", "MacOS", "BettingRoyaleLauncher")));
        Assert.False(Directory.Exists(staged));
        Assert.Empty(Directory.EnumerateDirectories(root, Bundle + ".previous-*"));
        Assert.Contains("launcher bundle replaced", System.IO.File.ReadAllText(log));
    }

    private (ReleaseManifest Manifest, Dictionary<string, byte[]> Server) Release(string version, string exe, string dll, string plist)
    {
        var files = new (string Path, byte[] Content, bool Executable)[]
        {
            (ExecutablePath, Bytes(exe), true), (DllPath, Bytes(dll), true), (PlistPath, Bytes(plist), false),
        };
        var manifest = MacManifest(version, files) with { Product = LauncherSelfUpdater.Product };
        manifest = manifest with
        {
            Files = manifest.Files.Select(file => file with { Url = $"releases/{version}/{string.Join('/', file.Path.Split('/').Select(Uri.EscapeDataString))}" }).ToArray(),
        };
        var server = new Dictionary<string, byte[]>
        {
            [manifestUri.AbsoluteUri] = JsonSerializer.SerializeToUtf8Bytes(manifest, LauncherJson.Options),
        };
        foreach (var (file, content) in manifest.Files.Zip(files.Select(item => item.Content)))
            server[new Uri(manifestUri, file.Url).AbsoluteUri] = content;
        return (manifest, server);
    }

    private string InstallBundle(string exe, string dll, string plist)
    {
        var bundle = Path.Combine(root, Bundle);
        Directory.CreateDirectory(Path.Combine(bundle, "Contents", "MacOS"));
        System.IO.File.WriteAllText(Path.Combine(bundle, "Contents", "MacOS", "BettingRoyaleLauncher"), exe);
        System.IO.File.WriteAllText(Path.Combine(bundle, "Contents", "MacOS", "BettingRoyaleLauncher.dll"), dll);
        System.IO.File.WriteAllText(Path.Combine(bundle, "Contents", "Info.plist"), plist);
        return bundle;
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

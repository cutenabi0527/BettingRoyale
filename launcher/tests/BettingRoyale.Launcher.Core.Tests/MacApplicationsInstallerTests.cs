using System.Diagnostics;
using BettingRoyale.Launcher.Core;

namespace BettingRoyale.Launcher.Core.Tests;

/// <summary>macOS 런처 응용 프로그램 폴더 설치(S15P21A606-323). 판정은 어느 OS 에서나, 옮기기 스크립트는 Unix 에서 검사한다.</summary>
public sealed class MacApplicationsInstallerTests : IDisposable
{
    private const string Home = "/Users/tester";
    private const string Bundle = "BettingRoyale Launcher Preview.app";

    private readonly string root = Path.Combine(Path.GetTempPath(), "BettingRoyaleInstallTests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("/Applications/" + Bundle, true)]
    [InlineData("/Applications/Games/" + Bundle, true)]
    [InlineData(Home + "/Applications/" + Bundle, true)]
    [InlineData(Home + "/Downloads/" + Bundle, false)]
    [InlineData(Home + "/Desktop/" + Bundle, false)]
    [InlineData("/Volumes/BettingRoyale Launcher/" + Bundle, false)]
    [InlineData("/ApplicationsOld/" + Bundle, false)]
    public void InstalledOnlyUnderApplicationsFolders(string bundle, bool expected)
    {
        Assert.Equal(expected, MacApplicationsInstaller.IsInstalled(bundle, Home));
    }

    [Fact]
    public void TranslocatedPathIsRecognized()
    {
        Assert.True(MacApplicationsInstaller.IsTranslocated(
            "/private/var/folders/x9/abc/T/AppTranslocation/89D42538-C18C-48FD-84F0-03FBDC059764/d/" + Bundle));
        Assert.False(MacApplicationsInstaller.IsTranslocated(Home + "/Downloads/" + Bundle));
    }

    [Fact]
    public void PlanPrefersSystemApplicationsAndMovesFromUserFolders()
    {
        var plan = MacApplicationsInstaller.Plan(Home + "/Downloads/" + Bundle, Home, _ => true);

        Assert.Equal(Home + "/Downloads/" + Bundle, plan.SourceBundle);
        Assert.Equal("/Applications/" + Bundle, plan.TargetBundle);
        Assert.False(plan.Copy);
    }

    [Fact]
    public void PlanFallsBackToUserApplicationsWhenSystemFolderIsNotWritable()
    {
        var plan = MacApplicationsInstaller.Plan(Home + "/Desktop/" + Bundle, Home, directory => directory != "/Applications");

        Assert.Equal(Home + "/Applications/" + Bundle, plan.TargetBundle);
    }

    [Fact]
    public void PlanCopiesFromDiskImagesInsteadOfMoving()
    {
        var plan = MacApplicationsInstaller.Plan("/Volumes/BettingRoyale Launcher/" + Bundle, Home, _ => true);

        Assert.True(plan.Copy);
        Assert.Equal("/Applications/" + Bundle, plan.TargetBundle);
    }

    [Fact]
    public void PlanKeepsAnAlreadyInstalledOriginalInPlace()
    {
        // 코드로 옮겨 격리 속성이 남은 앱은 제자리에 있어도 번역된 위치에서 돈다 — 옮기지 않고 제자리에서 정리한다.
        var plan = MacApplicationsInstaller.Plan(Home + "/Applications/" + Bundle, Home, _ => true);

        Assert.Equal(plan.SourceBundle, plan.TargetBundle);
    }

    // 판정은 macOS 경로의 POSIX 규칙만 쓴다 — Windows 에서 돌려도 드라이브·'\' 가 붙지 않는다(S15P21A606-336 — Jenkins Live #39 의 Windows 런처 빌드).
    [Theory]
    [InlineData(Home + "/Downloads/../Applications/" + Bundle, true)]
    [InlineData("//Applications/./" + Bundle + "/", true)]
    [InlineData(Home + "/Applications/../Desktop/" + Bundle, false)]
    public void PathsAreNormalizedWithPosixRules(string bundle, bool expected)
    {
        Assert.Equal(expected, MacApplicationsInstaller.IsInstalled(bundle, Home));
    }

    [Fact]
    public void PlanUsesPosixSeparatorsOnEveryOperatingSystem()
    {
        var plan = MacApplicationsInstaller.Plan(Home + "/Desktop/./" + Bundle + "/", Home + "/", directory => directory != "/Applications");

        Assert.Equal(Home + "/Desktop/" + Bundle, plan.SourceBundle);
        Assert.Equal(Home + "/Applications/" + Bundle, plan.TargetBundle);
        Assert.DoesNotContain('\\', plan.SourceBundle + plan.TargetBundle);
    }

    [Fact]
    public void PlanRejectsNonBundles()
    {
        Assert.Throws<ArgumentException>(() => MacApplicationsInstaller.Plan(Home + "/Downloads/launcher", Home, _ => true));
    }

    [UnixFact, System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task MoveScriptMovesBundleTrashesOldOneAndRelaunchesNewLocation()
    {
        var home = Path.Combine(root, "home");
        var source = MakeBundle(Path.Combine(home, "Downloads"), "new");
        var applications = Path.Combine(root, "Applications");
        MakeBundle(applications, "old");
        var target = Path.Combine(applications, Bundle);

        var (exitCode, relaunched, log) = await RunScriptAsync(home, new MacInstallPlan(source, target, Copy: false));

        Assert.Equal(0, exitCode);
        Assert.Equal("new", Marker(target));
        Assert.False(Directory.Exists(source));
        var trashed = Assert.Single(Directory.EnumerateDirectories(Path.Combine(home, ".Trash")));
        Assert.Equal("old", Marker(trashed));
        Assert.Equal(target, relaunched);
        Assert.Contains("launcher installed to Applications", log);
    }

    [UnixFact, System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task MoveScriptRelaunchesOriginalWhenTargetCannotBeCreated()
    {
        var home = Path.Combine(root, "home");
        var source = MakeBundle(Path.Combine(home, "Downloads"), "new");
        // 파일이 폴더 자리를 막고 있으면 mkdir -p 가 실패한다 — 권한 없는 폴더와 같은 경로를 탄다.
        var blocker = Path.Combine(root, "blocked");
        File.WriteAllText(blocker, "not a directory");

        var (exitCode, relaunched, log) = await RunScriptAsync(home,
            new MacInstallPlan(source, Path.Combine(blocker, "Applications", Bundle), Copy: false));

        Assert.NotEqual(0, exitCode);
        Assert.True(Directory.Exists(source));
        Assert.Equal(source, relaunched);
        Assert.Contains("BRL-B-INSTALL-ACCESS", log);
    }

    [UnixFact, System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task MoveScriptLeavesInPlaceBundleAndRelaunchesIt()
    {
        var home = Path.Combine(root, "home");
        var bundle = MakeBundle(Path.Combine(root, "Applications"), "same");

        var (exitCode, relaunched, _) = await RunScriptAsync(home, new MacInstallPlan(bundle, bundle, Copy: false));

        Assert.Equal(0, exitCode);
        Assert.Equal("same", Marker(bundle));
        Assert.Equal(bundle, relaunched);
        Assert.False(Directory.Exists(Path.Combine(home, ".Trash")));
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private async Task<(int ExitCode, string Relaunched, string Log)> RunScriptAsync(string home, MacInstallPlan plan)
    {
        Directory.CreateDirectory(home);
        var script = MacApplicationsInstaller.WriteMoveScript(Path.Combine(root, "scripts"));
        var log = Path.Combine(root, "install.log");
        var relaunchRecord = Path.Combine(root, "relaunched.txt");
        var relaunch = Path.Combine(root, "relaunch.sh");
        File.WriteAllText(relaunch, $"#!/bin/sh\nprintf '%s' \"$1\" > '{relaunchRecord}'\n");
        File.SetUnixFileMode(relaunch, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var exited = Process.Start(new ProcessStartInfo("/bin/sh", "-c true") { UseShellExecute = false })!;
        await exited.WaitForExitAsync();

        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        foreach (var argument in new[] { script, exited.Id.ToString(), plan.SourceBundle, plan.TargetBundle, plan.Copy ? "copy" : "move", log, relaunch })
            start.ArgumentList.Add(argument);
        start.Environment["HOME"] = home;
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync();
        return (process.ExitCode,
            File.Exists(relaunchRecord) ? File.ReadAllText(relaunchRecord) : string.Empty,
            File.Exists(log) ? File.ReadAllText(log) : string.Empty);
    }

    private static string MakeBundle(string directory, string marker)
    {
        var bundle = Path.Combine(directory, Bundle);
        Directory.CreateDirectory(Path.Combine(bundle, "Contents", "MacOS"));
        File.WriteAllText(Path.Combine(bundle, "Contents", "MacOS", "marker"), marker);
        return bundle;
    }

    private static string Marker(string bundle) => File.ReadAllText(Path.Combine(bundle, "Contents", "MacOS", "marker"));

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

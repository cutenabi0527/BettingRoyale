using System.Text.Json;
using BettingRoyale.Launcher.Core;

namespace BettingRoyale.Launcher.Core.Tests;

/// <summary>ManifestTool 의 --platform / --executable-glob (S15P21A606-312, Mac 계약 7절).</summary>
[Collection(ManifestToolCollection.Name)]
public sealed class ManifestToolTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "BettingRoyaleManifestToolTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MacManifestMarksExecutablesFromMainPathDefaultRuleAndGlobs()
    {
        var build = Path.Combine(root, "build");
        Write(build, "BettingRoyale.app/Contents/MacOS/BettingRoyale", "mach-o");
        Write(build, "BettingRoyale.app/Contents/MacOS/helper", "helper");
        Write(build, "BettingRoyale.app/Contents/Frameworks/Crash.framework/crashpad_handler", "crash");
        Write(build, "BettingRoyale.app/Contents/Frameworks/UnityPlayer.dylib", "dylib");
        Write(build, "BettingRoyale.app/Contents/Info.plist", "<plist/>");
        Write(build, "BettingRoyale.app/Contents/.DS_Store", "finder");
        var output = Path.Combine(root, "manifest.json");

        var exit = await ManifestTool.Program.Main([
            "--root", build, "--output", output, "--product", "BettingRoyale", "--version", "v1",
            "--platform", "macos-arm64",
            "--executable", "BettingRoyale.app/Contents/MacOS/BettingRoyale",
            "--executable-glob", "BettingRoyale.app/Contents/MacOS/*",
            "--executable-glob", "BettingRoyale.app/Contents/Frameworks/**/crashpad_handler",
            "--url-prefix", "releases/v1",
        ]);

        Assert.Equal(0, exit);
        var manifest = Read(output);
        manifest.Validate();
        Assert.Equal(ReleasePlatform.MacOSArm64, manifest.Platform);
        Assert.Equal("BettingRoyale.app/Contents/MacOS/BettingRoyale", manifest.Executable);
        var flags = manifest.Files.ToDictionary(file => file.Path, file => file.Executable);
        Assert.True(flags["BettingRoyale.app/Contents/MacOS/BettingRoyale"]);
        Assert.True(flags["BettingRoyale.app/Contents/MacOS/helper"]);
        Assert.True(flags["BettingRoyale.app/Contents/Frameworks/Crash.framework/crashpad_handler"]);
        Assert.Null(flags["BettingRoyale.app/Contents/Frameworks/UnityPlayer.dylib"]);
        Assert.Null(flags["BettingRoyale.app/Contents/Info.plist"]);
        // 번들 안의 파일은 서명 봉인 대상이라 .DS_Store 도 싣는다(S15P21A606-318).
        Assert.Null(flags["BettingRoyale.app/Contents/.DS_Store"]);
    }

    [Fact]
    public async Task MacManifestKeepsEverySealedFileInsideAppBundle()
    {
        var build = Path.Combine(root, "build");
        Write(build, "BettingRoyale.app/Contents/MacOS/BettingRoyale", "mach-o");
        Write(build, "BettingRoyale.app/Contents/MonoBleedingEdge/etc/mono/mconfig/config.xml", "<config/>");
        Write(build, "BettingRoyale.app/Contents/Resources/Data/StreamingAssets/aa/AddressablesLink/link.xml", "<linker/>");
        Write(build, "BettingRoyale.app/Contents/Resources/Data/Managed/Game.pdb", "pdb");
        Write(build, "BettingRoyale.app/Contents/Resources/build.log", "log");
        // 번들 밖은 기존 제외 규칙 그대로다.
        Write(build, "notes.xml", "<doc/>");
        Write(build, "BettingRoyale_BurstDebugInformation_DoNotShip/lib.txt", "debug");
        // Mac Burst 디버그 폴더는 번들 모양을 한 번 더 품는다 — 그래도 번들이 아니다(Jenkins 빌드 217).
        Write(build, "BettingRoyale_BurstDebugInformation_DoNotShip/BettingRoyale.app/Contents/Plugins/lib_burst_generated.txt", "debug");
        Write(build, ".DS_Store", "finder");
        var output = Path.Combine(root, "manifest.json");

        var exit = await ManifestTool.Program.Main([
            "--root", build, "--output", output, "--product", "BettingRoyale", "--version", "v1",
            "--platform", "macos-arm64",
            "--executable", "BettingRoyale.app/Contents/MacOS/BettingRoyale",
            "--url-prefix", "releases/v1",
        ]);

        Assert.Equal(0, exit);
        var paths = Read(output).Files.Select(file => file.Path).ToHashSet();
        Assert.Contains("BettingRoyale.app/Contents/MonoBleedingEdge/etc/mono/mconfig/config.xml", paths);
        Assert.Contains("BettingRoyale.app/Contents/Resources/Data/StreamingAssets/aa/AddressablesLink/link.xml", paths);
        Assert.Contains("BettingRoyale.app/Contents/Resources/Data/Managed/Game.pdb", paths);
        Assert.Contains("BettingRoyale.app/Contents/Resources/build.log", paths);
        Assert.DoesNotContain("notes.xml", paths);
        Assert.DoesNotContain("BettingRoyale_BurstDebugInformation_DoNotShip/lib.txt", paths);
        Assert.DoesNotContain("BettingRoyale_BurstDebugInformation_DoNotShip/BettingRoyale.app/Contents/Plugins/lib_burst_generated.txt", paths);
        Assert.DoesNotContain(".DS_Store", paths);
    }

    [Fact]
    public async Task WindowsInvocationKeepsLegacyManifestShape()
    {
        var build = Path.Combine(root, "build");
        Write(build, "BettingRoyale.exe", "pe");
        Write(build, "BettingRoyale_Data/data.unity3d", "data");
        var output = Path.Combine(root, "manifest.json");

        // package-game-release.ps1 의 기존 호출 그대로다(--platform 없음). --executable-glob 은 Windows 에서 무시된다.
        var exit = await ManifestTool.Program.Main([
            "--root", build, "--output", output, "--product", "BettingRoyale", "--version", "v1",
            "--executable", "BettingRoyale.exe", "--executable-glob", "*.exe", "--url-prefix", "releases/v1",
        ]);

        Assert.Equal(0, exit);
        var json = await File.ReadAllTextAsync(output);
        Assert.DoesNotContain("\"platform\"", json);
        Assert.DoesNotContain("\"executable\": true", json);
        var manifest = Read(output);
        Assert.Equal(ReleasePlatform.WindowsX64, manifest.EffectivePlatform);
        Assert.All(manifest.Files, file => Assert.Null(file.Executable));
    }

    [Fact]
    public async Task RejectsUnknownPlatform()
    {
        var build = Path.Combine(root, "build");
        Write(build, "BettingRoyale.exe", "pe");
        var exit = await ManifestTool.Program.Main([
            "--root", build, "--output", Path.Combine(root, "manifest.json"), "--product", "BettingRoyale", "--version", "v1",
            "--platform", "linux-x64", "--executable", "BettingRoyale.exe", "--url-prefix", "releases/v1",
        ]);
        Assert.Equal(1, exit);
    }

    private static ReleaseManifest Read(string path) =>
        JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(path), LauncherJson.Options)!;

    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

/// <summary>ManifestTool 은 Console 에 쓰므로 병렬 실행하지 않는다.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ManifestToolCollection
{
    public const string Name = "ManifestTool";
}

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BettingRoyale.Launcher.Core;

namespace BettingRoyale.Launcher.Core.Tests;

/// <summary>manifest platform/executable 필드와 Unix 파일 권한(S15P21A606-312).</summary>
public sealed class PlatformManifestTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "BettingRoyaleLauncherTests", Guid.NewGuid().ToString("N"));
    private readonly Uri manifestUri = new("https://download.test/game/macos/manifest.json");

    private const string MacExecutable = "BettingRoyale.app/Contents/MacOS/BettingRoyale";

    [Fact]
    public void LegacyWindowsManifestWithoutNewFieldsKeepsWorking()
    {
        // S15P21A606-293 시점의 Windows manifest 모양 그대로다. platform·executable 이 없다.
        const string legacy = """
            {
              "schemaVersion": 1,
              "product": "BettingRoyale",
              "version": "v0.3.7",
              "executable": "BettingRoyale.exe",
              "channel": "direct",
              "publishedAt": "2026-09-22T00:00:00+00:00",
              "patchNotes": ["hello"],
              "files": [
                { "path": "BettingRoyale.exe", "url": "releases/v0.3.7/BettingRoyale.exe", "size": 4,
                  "sha256": "88d4266fd4e6338d13b845fcf289579d209c897823b9217da3e161936f031589" }
              ]
            }
            """;
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(legacy, LauncherJson.Options)!;

        manifest.Validate();
        manifest.EnsurePlatform(ReleasePlatform.WindowsX64);
        Assert.Null(manifest.Platform);
        Assert.Equal(ReleasePlatform.WindowsX64, manifest.EffectivePlatform);
        Assert.Null(manifest.Files[0].Executable);
        Assert.Throws<InvalidDataException>(() => manifest.EnsurePlatform(ReleasePlatform.MacOSArm64));

        // 다시 쓸 때도 새 필드를 만들지 않는다 — 구 런처·게시 검증기가 보는 모양이 그대로다.
        var written = JsonSerializer.Serialize(manifest, LauncherJson.Options);
        Assert.DoesNotContain("\"platform\"", written);
        Assert.DoesNotContain("\"executable\": true", written);
    }

    [Fact]
    public void MacManifestParsesPlatformAndExecutableFlags()
    {
        var json = $$"""
            {
              "schemaVersion": 1, "product": "BettingRoyale", "version": "v1", "platform": "macos-arm64",
              "executable": "{{MacExecutable}}", "publishedAt": "2026-09-24T00:00:00+00:00",
              "files": [
                { "path": "{{MacExecutable}}", "url": "releases/v1/a", "size": 1, "sha256": "{{new string('a', 64)}}", "executable": true },
                { "path": "BettingRoyale.app/Contents/Info.plist", "url": "releases/v1/b", "size": 1, "sha256": "{{new string('b', 64)}}" }
              ]
            }
            """;
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(json, LauncherJson.Options)!;

        manifest.Validate();
        Assert.Equal(ReleasePlatform.MacOSArm64, manifest.EffectivePlatform);
        Assert.True(manifest.Files[0].Executable);
        Assert.Null(manifest.Files[1].Executable);
        Assert.Throws<InvalidDataException>(() => manifest.EnsurePlatform(ReleasePlatform.WindowsX64));
    }

    [Fact]
    public void UnknownPlatformIsRejected()
    {
        var manifest = MacManifest("v1", (MacExecutable, Bytes("x"), true)) with { Platform = "linux-x64" };
        Assert.Throws<InvalidDataException>(manifest.Validate);
    }

    [Fact]
    public void MacManifestRequiresExecutableFlagOnMainExecutable()
    {
        var manifest = MacManifest("v1", (MacExecutable, Bytes("x"), false));
        Assert.Throws<InvalidDataException>(manifest.Validate);
    }

    [Fact]
    public void DesktopRuntimeIsOnlyAllowedInWindowsManifests()
    {
        var runtime = new RuntimeRequirement
        {
            Version = "8.0.31",
            Url = "runtime/windowsdesktop-runtime-8.0.31-win-x64.exe",
            Size = 1,
            Sha256 = new string('c', 64),
        };
        var mac = MacManifest("v1", (MacExecutable, Bytes("x"), true)) with { Runtime = runtime };
        Assert.Throws<InvalidDataException>(mac.Validate);

        var windows = mac with
        {
            Platform = null,
            Executable = "BettingRoyale.exe",
            Files = [File("BettingRoyale.exe", Bytes("x"), null)],
        };
        windows.Validate();
    }

    [Fact]
    public async Task ManifestClientRejectsManifestForAnotherPlatform()
    {
        var other = ReleasePlatform.Current == ReleasePlatform.MacOSArm64 ? ReleasePlatform.WindowsX64 : ReleasePlatform.MacOSArm64;
        var manifest = other == ReleasePlatform.MacOSArm64
            ? MacManifest("v1", (MacExecutable, Bytes("x"), true))
            : new ReleaseManifest
            {
                Product = "BettingRoyale", Version = "v1", Executable = "BettingRoyale.exe",
                Files = [File("BettingRoyale.exe", Bytes("x"), null)],
            };
        using var http = new HttpClient(new StaticHandler(new Dictionary<string, byte[]>
        {
            [manifestUri.AbsoluteUri] = JsonSerializer.SerializeToUtf8Bytes(manifest, LauncherJson.Options),
        }));
        var client = new ManifestClient(http);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => client.GetAsync(manifestUri, "BettingRoyale"));
        Assert.Equal("BRL-L-MANIFEST-FORMAT", LauncherDiagnostics.Describe(error, "L", "CHECK").Code);
        var accepted = await client.GetAsync(manifestUri, "BettingRoyale", other);
        Assert.Equal("v1", accepted.Version);
    }

    [Theory]
    [InlineData(ExecutablePattern.MacBundleDefault, "BettingRoyale.app/Contents/MacOS/BettingRoyale", true)]
    [InlineData(ExecutablePattern.MacBundleDefault, "BettingRoyale Launcher.app/Contents/MacOS/BettingRoyaleLauncher", true)]
    [InlineData(ExecutablePattern.MacBundleDefault, "BettingRoyale.app/Contents/Frameworks/UnityPlayer.dylib", false)]
    [InlineData(ExecutablePattern.MacBundleDefault, "BettingRoyale.app/Contents/MacOS/sub/tool", false)]
    [InlineData(ExecutablePattern.MacBundleDefault, "Other/Contents/MacOS/tool", false)]
    [InlineData("BettingRoyale.app/Contents/**/crashpad_handler", "BettingRoyale.app/Contents/Frameworks/x/crashpad_handler", true)]
    [InlineData("BettingRoyale.app/Contents/MacOS/BettingRoyale", "bettingroyale.app/contents/macos/bettingroyale", true)]
    public void ExecutablePatternsMatchManifestPaths(string pattern, string path, bool expected) =>
        Assert.Equal(expected, ExecutablePattern.Matches(pattern, path));

    [Fact]
    public void SafePathAcceptsAppBundlePathsButNotTraversal()
    {
        Assert.Equal("BettingRoyale Launcher.app/Contents/MacOS/BettingRoyaleLauncher",
            SafePath.NormalizeRelative("BettingRoyale Launcher.app/Contents/MacOS/BettingRoyaleLauncher"));
        Assert.Throws<InvalidDataException>(() => SafePath.NormalizeRelative("BettingRoyale.app/../../evil"));
        Assert.Throws<InvalidDataException>(() => SafePath.NormalizeRelative("/Applications/BettingRoyale.app/Contents/Info.plist"));
    }

    [Fact]
    public void MacInstallRootFollowsContract()
    {
        if (!OperatingSystem.IsMacOS()) return; // macOS 전용 경로 계약.
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support",
            "BettingRoyale", "Preview");
        Assert.Equal(expected, LauncherPaths.ProductRootFor("preview"));
        Assert.Equal("game", LauncherPlatform.GameDirectoryName);
    }

    [Fact]
    public void MacBundleReadsRuntimeConfigurationFromResources()
    {
        // .app 의 Contents/MacOS 에는 Mach-O 만 둔다 — 설정은 Contents/Resources 에 있다.
        var contents = Path.Combine(root, "BettingRoyale Launcher.app", "Contents");
        var macOs = Path.Combine(contents, "MacOS");
        var resources = Path.Combine(contents, "Resources");
        Directory.CreateDirectory(macOs);
        Directory.CreateDirectory(resources);
        Assert.Equal(macOs, LauncherRuntimeConfiguration.ResolveDirectory(macOs)); // Resources 에 없으면 기존 위치.
        System.IO.File.WriteAllText(Path.Combine(resources, "launcher.runtime.json"), """
            { "distributionChannel": "direct", "gameManifestUrl": "https://example.test/game/macos/manifest.json",
              "gameServerHealthUrl": "https://example.test/health", "launcherManifestUrl": "https://example.test/launcher/macos/manifest.json" }
            """);

        var directory = LauncherRuntimeConfiguration.ResolveDirectory(macOs + Path.DirectorySeparatorChar);
        Assert.Equal(resources, directory);
        Assert.Equal("https://example.test/launcher/macos/manifest.json", LauncherRuntimeConfiguration.Load(directory).LauncherManifestUrl);
        // Windows·번들 밖 실행은 실행 파일 폴더 그대로다.
        var plain = Path.Combine(root, "bin");
        Assert.Equal(plain, LauncherRuntimeConfiguration.ResolveDirectory(plain));
    }

    [UnixFact, System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task PatchAppliesUnixModesFromManifest()
    {
        var binary = Bytes("mach-o");
        var plist = Bytes("<plist/>");
        var server = new Dictionary<string, byte[]>
        {
            ["https://download.test/game/macos/releases/v1/BettingRoyale.app/Contents/MacOS/BettingRoyale"] = binary,
            ["https://download.test/game/macos/releases/v1/BettingRoyale.app/Contents/Info.plist"] = plist,
        };
        using var http = new HttpClient(new StaticHandler(server));
        var engine = new FilePatchEngine(http);
        var manifest = MacManifest("v1", (MacExecutable, binary, true), ("BettingRoyale.app/Contents/Info.plist", plist, false));

        await engine.ApplyAsync(manifestUri, root, await engine.AnalyzeAsync(root, manifest));

        var executable = Path.Combine(root, "BettingRoyale.app", "Contents", "MacOS", "BettingRoyale");
        var info = Path.Combine(root, "BettingRoyale.app", "Contents", "Info.plist");
        Assert.Equal(LauncherPlatform.ExecutableMode, System.IO.File.GetUnixFileMode(executable));
        Assert.Equal(LauncherPlatform.RegularMode, System.IO.File.GetUnixFileMode(info));
        Assert.Equal(InstallState.Ready, (await engine.AnalyzeAsync(root, manifest)).State);

        // 내용은 같지만 실행 권한을 잃으면 시작할 수 없으므로 다시 받아 권한을 되돌린다.
        System.IO.File.SetUnixFileMode(executable, LauncherPlatform.RegularMode);
        var repair = await engine.AnalyzeAsync(root, manifest);
        Assert.Equal(InstallState.RepairRequired, repair.State);
        Assert.Equal(MacExecutable, Assert.Single(repair.FilesToDownload).Path);
        await engine.ApplyAsync(manifestUri, root, repair);
        Assert.Equal(LauncherPlatform.ExecutableMode, System.IO.File.GetUnixFileMode(executable));

        // 이번에 받지 않은 파일도 manifest 기준 권한으로 맞춘다.
        System.IO.File.SetUnixFileMode(info, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        System.IO.File.SetUnixFileMode(executable, LauncherPlatform.RegularMode);
        await engine.ApplyAsync(manifestUri, root, await engine.AnalyzeAsync(root, manifest));
        Assert.Equal(LauncherPlatform.RegularMode, System.IO.File.GetUnixFileMode(info));
    }

    internal static ReleaseManifest MacManifest(string version, params (string Path, byte[] Content, bool Executable)[] files) => new()
    {
        Product = "BettingRoyale",
        Version = version,
        Platform = ReleasePlatform.MacOSArm64,
        Executable = files[0].Path,
        PublishedAt = DateTimeOffset.UtcNow,
        Files = files.Select(item => File(item.Path, item.Content, item.Executable ? true : null, $"releases/{version}/{item.Path}")).ToArray(),
    };

    internal static ReleaseFile File(string path, byte[] content, bool? executable, string? url = null) => new()
    {
        Path = path,
        Url = url ?? $"releases/v1/{path}",
        Size = content.Length,
        Sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
        Executable = executable,
    };

    internal static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

/// <summary>Unix(macOS·Linux CI) 에서만 도는 테스트. Windows 에는 파일 모드가 없다.</summary>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Unix 파일 권한 전용";
    }
}

/// <summary>URL → 본문 사전으로 응답하는 가짜 서버. 요청한 URL 을 기록한다.</summary>
internal sealed class StaticHandler(IReadOnlyDictionary<string, byte[]> responses) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.AbsoluteUri ?? string.Empty;
        lock (Requests) Requests.Add(url);
        return Task.FromResult(responses.TryGetValue(url, out var content)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

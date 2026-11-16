using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BettingRoyale.Launcher.Core;

namespace BettingRoyale.Launcher.Core.Tests;

public sealed class DesktopRuntimeInstallerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "br-runtime-" + Guid.NewGuid().ToString("N"));

    private static RuntimeRequirement Requirement(byte[] installer, string version = "8.0.31") => new()
    {
        Version = version,
        Url = $"runtime/windowsdesktop-runtime-{version}-win-x64.exe",
        Size = installer.Length,
        Sha256 = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant(),
    };

    private string FakeSharedRoot(params string[] installedVersions)
    {
        var shared = Path.Combine(root, "dotnet-" + Guid.NewGuid().ToString("N"), "shared");
        foreach (var version in installedVersions)
        {
            var directory = Path.Combine(shared, RuntimeRequirement.WindowsDesktop, version);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, RuntimeRequirement.WindowsDesktop + ".deps.json"), "{}");
        }

        return shared;
    }

    [Theory]
    [InlineData("8.0.31", true)]
    [InlineData("8.0.40", true)]
    [InlineData("8.0.30", false)]
    [InlineData("9.0.5", false)]
    [InlineData("7.0.20", false)]
    public void SatisfiedOnlyBySameMajorAtOrAboveRequiredVersion(string installed, bool expected)
    {
        var shared = FakeSharedRoot(installed);
        Assert.Equal(expected, DesktopRuntimeInstaller.IsSatisfied([shared], Requirement(new byte[1])));
    }

    [Fact]
    public void DirectoryWithoutDepsJsonIsNotAnInstalledRuntime()
    {
        var shared = FakeSharedRoot();
        Directory.CreateDirectory(Path.Combine(shared, RuntimeRequirement.WindowsDesktop, "8.0.31"));
        Assert.False(DesktopRuntimeInstaller.IsSatisfied([shared], Requirement(new byte[1])));
        Assert.False(DesktopRuntimeInstaller.IsSatisfied([Path.Combine(root, "missing")], Requirement(new byte[1])));
    }

    [Fact]
    public void ManifestRoundTripsRuntimeAndOldReadersIgnoreIt()
    {
        var manifest = new ReleaseManifest
        {
            Product = "BettingRoyaleLauncher", Version = "v1", Executable = "Launcher.exe",
            Files = [new ReleaseFile { Path = "Launcher.exe", Url = "releases/v1/Launcher.exe", Size = 1, Sha256 = new string('a', 64) }],
            Runtime = Requirement(new byte[] { 1, 2, 3 }),
        };
        manifest.Validate();
        var json = JsonSerializer.Serialize(manifest, LauncherJson.Options);
        var parsed = JsonSerializer.Deserialize<ReleaseManifest>(json, LauncherJson.Options)!;
        Assert.Equal("8.0.31", parsed.Runtime!.Version);
        Assert.Equal("windowsdesktop-runtime-8.0.31-win-x64.exe", parsed.Runtime.AsReleaseFile().Path);

        // A manifest without the field still validates: the requirement is optional in schema 1.
        var legacy = JsonSerializer.Deserialize<ReleaseManifest>(json.Replace("\"runtime\"", "\"unknownField\""), LauncherJson.Options)!;
        Assert.Null(legacy.Runtime);
        legacy.Validate();
    }

    [Theory]
    [InlineData("Microsoft.NETCore.App", "8.0.31", "runtime/a.exe")]
    [InlineData("Microsoft.WindowsDesktop.App", "8", "runtime/a.exe")]
    [InlineData("Microsoft.WindowsDesktop.App", "8.0.31", "https://evil.example/a.exe")]
    [InlineData("Microsoft.WindowsDesktop.App", "8.0.31", "runtime/nested/a.exe")]
    [InlineData("Microsoft.WindowsDesktop.App", "8.0.31", "runtime/a.msi")]
    public void InvalidRuntimeRequirementsAreRejected(string name, string version, string url)
    {
        var requirement = new RuntimeRequirement { Name = name, Version = version, Url = url, Size = 10, Sha256 = new string('b', 64) };
        Assert.Throws<InvalidDataException>(requirement.Validate);
    }

    [Fact]
    public async Task InstallsOnlyWhenMissingAndVerifiesBeforeRunning()
    {
        var installerBytes = Encoding.UTF8.GetBytes(new string('r', 64 * 1024));
        var requirement = Requirement(installerBytes);
        await using var server = new RuntimeServer(installerBytes);
        using var http = new HttpClient();
        var shared = FakeSharedRoot();
        var runs = new List<string>();
        var installer = new DesktopRuntimeInstaller(http)
        {
            SharedFrameworkRoots = [shared],
            RunInstaller = (path, _) =>
            {
                runs.Add(path);
                // Simulate the installer laying down the shared framework.
                var directory = Path.Combine(shared, RuntimeRequirement.WindowsDesktop, "8.0.31");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, RuntimeRequirement.WindowsDesktop + ".deps.json"), "{}");
                return Task.FromResult(3010);
            },
        };
        var cache = Path.Combine(root, "cache");

        Assert.True(await installer.EnsureAsync(server.ManifestUri, requirement, cache));
        Assert.Single(runs);
        Assert.Equal(installerBytes, await File.ReadAllBytesAsync(runs[0]));
        Assert.Equal(1, server.Downloads);

        // Second run: already satisfied, no download, no installer.
        Assert.False(await installer.EnsureAsync(server.ManifestUri, requirement, cache));
        Assert.Single(runs);
        Assert.Equal(1, server.Downloads);
        Assert.False(await installer.EnsureAsync(server.ManifestUri, null, cache));
    }

    [Fact]
    public async Task TamperedInstallerIsNeverRun()
    {
        var installerBytes = Encoding.UTF8.GetBytes(new string('r', 4096));
        var requirement = Requirement(installerBytes);
        await using var server = new RuntimeServer(Encoding.UTF8.GetBytes(new string('x', 4096)));
        using var http = new HttpClient();
        var runs = 0;
        var installer = new DesktopRuntimeInstaller(http)
        {
            SharedFrameworkRoots = [FakeSharedRoot()],
            RunInstaller = (_, _) => { runs++; return Task.FromResult(0); },
        };

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => installer.EnsureAsync(server.ManifestUri, requirement, Path.Combine(root, "cache")));
        Assert.Equal(0, runs);
        Assert.Equal("BRL-B-RUNTIME-FORMAT", LauncherDiagnostics.Describe(exception, "B", "STARTUP").Code);
        Assert.Empty(Directory.Exists(Path.Combine(root, "cache")) ? Directory.GetFiles(Path.Combine(root, "cache")) : []);
    }

    [Fact]
    public async Task FailedInstallerExitCodeIsReportedAsRuntimeStage()
    {
        var installerBytes = Encoding.UTF8.GetBytes(new string('r', 4096));
        await using var server = new RuntimeServer(installerBytes);
        using var http = new HttpClient();
        var installer = new DesktopRuntimeInstaller(http)
        {
            SharedFrameworkRoots = [FakeSharedRoot()],
            RunInstaller = (_, _) => Task.FromResult(1603),
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => installer.EnsureAsync(server.ManifestUri, Requirement(installerBytes), Path.Combine(root, "cache")));
        var failure = LauncherDiagnostics.Describe(exception, "B", "STARTUP");
        Assert.Equal("BRL-B-RUNTIME-STATE", failure.Code);
        Assert.Contains(".NET Desktop Runtime", failure.Message);
    }

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    /// <summary>
    /// 포트 0 에 직접 바인딩하는 최소 HTTP 서버. HttpListener 를 쓰지 않는 이유:
    /// 빈 포트를 미리 고르는 방식은 고른 뒤 바인딩까지 경합이 있고, Unix 의 managed HttpListener 는
    /// Stop() 뒤 Close() 가 이미 내려간 포트를 다시 바인딩해 병렬 실행 중 "Address already in use" 를 던진다.
    /// </summary>
    private sealed class RuntimeServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly Task worker;
        private readonly byte[] body;
        public int Downloads;

        public RuntimeServer(byte[] body)
        {
            this.body = body;
            listener.Start();
            ManifestUri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/launcher/manifest.json");
            worker = ServeAsync();
        }

        public Uri ManifestUri { get; }

        private async Task ServeAsync()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(stop.Token);
                    try { await RespondAsync(client); }
                    catch (IOException) when (!stop.IsCancellationRequested) { } // 클라이언트가 먼저 끊은 연결은 다음 요청에 영향이 없다.
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (SocketException) when (stop.IsCancellationRequested) { }
        }

        private async Task RespondAsync(TcpClient client)
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(stop.Token) ?? string.Empty;
            while (await reader.ReadLineAsync(stop.Token) is { Length: > 0 }) { }
            var path = requestLine.Split(' ') is [_, var target, ..] ? target : string.Empty;
            var found = path.StartsWith("/launcher/runtime/", StringComparison.Ordinal);
            if (found) Interlocked.Increment(ref Downloads);
            var payload = found ? body : [];
            var status = found ? "200 OK" : "404 Not Found";
            var headers = $"HTTP/1.1 {status}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), stop.Token);
            await stream.WriteAsync(payload, stop.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            listener.Stop();
            await worker;
            stop.Dispose();
        }
    }
}

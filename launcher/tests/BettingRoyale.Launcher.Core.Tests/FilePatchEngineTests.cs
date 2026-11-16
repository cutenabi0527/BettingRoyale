using System.Net;
using System.Security.Cryptography;
using System.Text;
using BettingRoyale.Launcher.Core;

namespace BettingRoyale.Launcher.Core.Tests;

public sealed class FilePatchEngineTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "BettingRoyaleLauncherTests", Guid.NewGuid().ToString("N"));
    private readonly Uri manifestUri = new("https://download.test/game/manifest.json");

    [Fact]
    public async Task InstallsUpdatesRemovesOnlyManagedStaleFilesAndRepairsCorruption()
    {
        var server = new Dictionary<string, byte[]>
        {
            ["https://download.test/game/releases/v1/BettingRoyale.exe"] = Bytes("game-v1"),
            ["https://download.test/game/releases/v1/old.bundle"] = Bytes("old"),
        };
        using var http = new HttpClient(new DictionaryHandler(server));
        var engine = new FilePatchEngine(http);
        var v1 = Manifest("v1", ("BettingRoyale.exe", "releases/v1/BettingRoyale.exe", server.Values.First()), ("old.bundle", "releases/v1/old.bundle", server.Values.Last()));

        var initialPlan = await engine.AnalyzeAsync(root, v1);
        Assert.Equal(InstallState.NotInstalled, initialPlan.State);
        await engine.ApplyAsync(manifestUri, root, initialPlan);
        Assert.Equal("game-v1", await File.ReadAllTextAsync(Path.Combine(root, "BettingRoyale.exe")));
        await File.WriteAllTextAsync(Path.Combine(root, "user-settings.json"), "keep");

        server["https://download.test/game/releases/v2/BettingRoyale.exe"] = Bytes("game-v2");
        server["https://download.test/game/releases/v2/new.bundle"] = Bytes("new");
        var v2 = Manifest(
            "v2",
            ("BettingRoyale.exe", "releases/v2/BettingRoyale.exe", server["https://download.test/game/releases/v2/BettingRoyale.exe"]),
            ("new.bundle", "releases/v2/new.bundle", server["https://download.test/game/releases/v2/new.bundle"]));
        var updatePlan = await engine.AnalyzeAsync(root, v2);
        Assert.Equal(InstallState.UpdateAvailable, updatePlan.State);
        Assert.Contains("old.bundle", updatePlan.StaleManagedFiles);
        await engine.ApplyAsync(manifestUri, root, updatePlan);

        Assert.Equal("game-v2", await File.ReadAllTextAsync(Path.Combine(root, "BettingRoyale.exe")));
        Assert.False(File.Exists(Path.Combine(root, "old.bundle")));
        Assert.True(File.Exists(Path.Combine(root, "new.bundle")));
        Assert.True(File.Exists(Path.Combine(root, "user-settings.json")));

        await File.WriteAllTextAsync(Path.Combine(root, "BettingRoyale.exe"), "corrupt");
        var repairPlan = await engine.AnalyzeAsync(root, v2);
        Assert.Equal(InstallState.RepairRequired, repairPlan.State);
        Assert.Single(repairPlan.FilesToDownload);
        await engine.ApplyAsync(manifestUri, root, repairPlan);
        Assert.Equal("game-v2", await File.ReadAllTextAsync(Path.Combine(root, "BettingRoyale.exe")));
    }

    [Fact]
    public async Task HashMismatchNeverReplacesExistingExecutable()
    {
        Directory.CreateDirectory(root);
        var executable = Path.Combine(root, "BettingRoyale.exe");
        await File.WriteAllTextAsync(executable, "known-good");
        var expected = Bytes("expected-new");
        var manifest = Manifest("v2", ("BettingRoyale.exe", "releases/v2/BettingRoyale.exe", expected));
        var server = new Dictionary<string, byte[]>
        {
            ["https://download.test/game/releases/v2/BettingRoyale.exe"] = Bytes("tampered"),
        };
        using var http = new HttpClient(new DictionaryHandler(server));
        var engine = new FilePatchEngine(http);
        var plan = await engine.AnalyzeAsync(root, manifest);

        await Assert.ThrowsAsync<InvalidDataException>(() => engine.ApplyAsync(manifestUri, root, plan));
        Assert.Equal("known-good", await File.ReadAllTextAsync(executable));
    }

    [Fact]
    public async Task FailedMultiFileDownloadKeepsPreviouslyInstalledRelease()
    {
        var server = new Dictionary<string, byte[]>
        {
            ["https://download.test/game/releases/v1/BettingRoyale.exe"] = Bytes("game-v1"),
        };
        using var http = new HttpClient(new DictionaryHandler(server));
        var engine = new FilePatchEngine(http);
        var v1 = Manifest(
            "v1",
            ("BettingRoyale.exe", "releases/v1/BettingRoyale.exe", server["https://download.test/game/releases/v1/BettingRoyale.exe"]));
        await engine.ApplyAsync(manifestUri, root, await engine.AnalyzeAsync(root, v1));

        server["https://download.test/game/releases/v2/BettingRoyale.exe"] = Bytes("game-v2");
        var v2 = Manifest(
            "v2",
            ("BettingRoyale.exe", "releases/v2/BettingRoyale.exe", server["https://download.test/game/releases/v2/BettingRoyale.exe"]),
            ("content/new.bundle", "releases/v2/content/new.bundle", Bytes("missing-on-server")));
        var plan = await engine.AnalyzeAsync(root, v2);

        await Assert.ThrowsAsync<HttpRequestException>(() => engine.ApplyAsync(manifestUri, root, plan));
        Assert.Equal("game-v1", await File.ReadAllTextAsync(Path.Combine(root, "BettingRoyale.exe")));
        var installed = await LocalReleaseStore.ReadManifestAsync(root);
        Assert.Equal("v1", installed?.Version);
    }

    [Fact]
    public void ManifestRejectsDuplicateAndCrossOriginFiles()
    {
        var bytes = Bytes("game");
        var duplicate = Manifest(
            "v1",
            ("BettingRoyale.exe", "releases/v1/a", bytes),
            ("BettingRoyale.exe", "releases/v1/b", bytes),
            validate: false);
        Assert.Throws<InvalidDataException>(duplicate.Validate);

        var external = CreateReleaseFile("BettingRoyale.exe", "https://evil.test/game.exe", bytes);
        Assert.Throws<InvalidDataException>(external.Validate);
    }

    [Fact]
    public async Task ManifestClientRejectsAnotherProduct()
    {
        var bytes = Bytes("game");
        var manifest = Manifest("v1", ("BettingRoyale.exe", "releases/v1/BettingRoyale.exe", bytes));
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(manifest, LauncherJson.Options);
        var server = new Dictionary<string, byte[]>
        {
            [manifestUri.AbsoluteUri] = payload,
        };
        using var http = new HttpClient(new DictionaryHandler(server));
        var client = new ManifestClient(http);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.GetAsync(manifestUri, "BettingRoyaleLauncher"));
    }

    [Fact]
    public async Task OfflineRefreshBlocksCorruptedLocalRelease()
    {
        var contents = Bytes("game-v1");
        var server = new Dictionary<string, byte[]>
        {
            ["https://download.test/game/releases/v1/BettingRoyale.exe"] = contents,
        };
        using (var onlineHttp = new HttpClient(new DictionaryHandler(server)))
        {
            var engine = new FilePatchEngine(onlineHttp);
            var manifest = Manifest(
                "v1",
                ("BettingRoyale.exe", "releases/v1/BettingRoyale.exe", contents));
            await engine.ApplyAsync(manifestUri, root, await engine.AnalyzeAsync(root, manifest));
        }

        await System.IO.File.WriteAllTextAsync(Path.Combine(root, "BettingRoyale.exe"), "corrupt");
        using var offlineHttp = new HttpClient(new ThrowingHandler());
        var channel = new DirectDistributionChannel(
            root,
            manifestUri,
            offlineHttp,
            new FileLauncherLog(Path.Combine(root, "logs", "launcher.log")));

        var snapshot = await channel.RefreshAsync();

        Assert.False(snapshot.ServerOnline);
        Assert.Equal(InstallState.RepairRequired, snapshot.State);
        Assert.Contains("복구", snapshot.StatusMessage);
    }

    private static ReleaseManifest Manifest(
        string version,
        (string Path, string Url, byte[] Content) file,
        bool validate = true) => Manifest(version, [file], validate);

    private static ReleaseManifest Manifest(
        string version,
        (string Path, string Url, byte[] Content) first,
        (string Path, string Url, byte[] Content) second,
        bool validate = true) => Manifest(version, [first, second], validate);

    private static ReleaseManifest Manifest(
        string version,
        IReadOnlyList<(string Path, string Url, byte[] Content)> files,
        bool validate)
    {
        var manifest = new ReleaseManifest
        {
            Product = "BettingRoyale",
            Version = version,
            Executable = "BettingRoyale.exe",
            PublishedAt = DateTimeOffset.UtcNow,
            Files = files.Select(item => CreateReleaseFile(item.Path, item.Url, item.Content)).ToArray(),
        };
        if (validate)
        {
            manifest.Validate();
        }

        return manifest;
    }

    private static ReleaseFile CreateReleaseFile(string path, string url, byte[] content) => new()
    {
        Path = path,
        Url = url,
        Size = content.Length,
        Sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
    };

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    private sealed class DictionaryHandler(IReadOnlyDictionary<string, byte[]> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null && responses.TryGetValue(request.RequestUri.AbsoluteUri, out var content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }
}

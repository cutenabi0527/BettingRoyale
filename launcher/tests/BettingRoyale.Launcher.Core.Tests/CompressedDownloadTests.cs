using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using BettingRoyale.Launcher.Core;

namespace BettingRoyale.Launcher.Core.Tests;

public sealed class CompressedDownloadTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "br-compression-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("gzip")]
    [InlineData("identity")]
    [InlineData(null)]
    public async Task HttpInstallRepairAndUpdatePreserveOriginalHashes(string? encoding)
    {
        await using var server = new ReleaseServer();
        using var http = new HttpClient();
        var engine = new FilePatchEngine(http);
        var bytes = Encoding.UTF8.GetBytes(new string('a', 256 * 1024));
        var manifest = Manifest("v1", bytes);
        var compressed = encoding == "gzip";
        server.Body = compressed ? Gzip(bytes) : bytes;
        server.Encoding = encoding;
        await engine.ApplyAsync(server.ManifestUri, root, await engine.AnalyzeAsync(root, manifest));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(root, "Game.exe")));
        Assert.Equal(InstallState.Ready, (await engine.AnalyzeAsync(root, manifest)).State);
        Assert.Contains("Accept-Encoding: gzip", server.LastRequest);
        if (compressed) Assert.True(server.Body.Length < bytes.Length / 10);

        await File.WriteAllTextAsync(Path.Combine(root, "Game.exe"), "damaged");
        var repair = await engine.AnalyzeAsync(root, manifest);
        Assert.Equal(InstallState.RepairRequired, repair.State);
        await engine.ApplyAsync(server.ManifestUri, root, repair);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(root, "Game.exe")));

        var updated = Encoding.UTF8.GetBytes(new string('b', 256 * 1024));
        var v2 = Manifest("v2", updated);
        server.Body = compressed ? Gzip(updated) : updated;
        await engine.ApplyAsync(server.ManifestUri, root, await engine.AnalyzeAsync(root, v2));
        Assert.Equal(updated, await File.ReadAllBytesAsync(Path.Combine(root, "Game.exe")));
        Assert.Empty((await engine.AnalyzeAsync(root, v2)).FilesToDownload);
    }

    [Fact]
    public async Task CancellationDuringDecompressionPreservesInstalledRelease()
    {
        await using var server = new ReleaseServer();
        using var http = new HttpClient();
        var engine = new FilePatchEngine(http);
        byte[] original = [1, 2, 3, 4];
        server.Body = Gzip(original);
        server.Encoding = "gzip";
        var v1 = Manifest("v1", original);
        await engine.ApplyAsync(server.ManifestUri, root, await engine.AnalyzeAsync(root, v1));
        var changed = new byte[1024 * 1024];
        server.Body = Gzip(changed);
        var v2 = Manifest("v2", changed);
        var plan = await engine.AnalyzeAsync(root, v2);
        using var cancel = new CancellationTokenSource();
        var progress = new CancelOnDownload(cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.ApplyAsync(server.ManifestUri, root, plan, progress, cancel.Token));
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(root, "Game.exe")));
        Assert.Equal("v1", (await LocalReleaseStore.ReadManifestAsync(root))?.Version);
        Assert.Empty(Directory.GetDirectories(Path.Combine(root, ".bettingroyale-patch")));
    }

    private sealed class CancelOnDownload(CancellationTokenSource cancel) : IProgress<PatchProgress>
    {
        public void Report(PatchProgress value) { if (value.Phase == "다운로드") cancel.Cancel(); }
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("truncated")]
    [InlineData("oversized")]
    [InlineData("crc")]
    [InlineData("unknown")]
    public async Task FailedCompressedDownloadPreservesInstalledRelease(string failure)
    {
        await using var server = new ReleaseServer();
        using var http = new HttpClient();
        var engine = new FilePatchEngine(http);
        byte[] original = [1, 2, 3, 4];
        server.Body = Gzip(original);
        server.Encoding = "gzip";
        var v1 = Manifest("v1", original);
        await engine.ApplyAsync(server.ManifestUri, root, await engine.AnalyzeAsync(root, v1));
        var v2 = Manifest("v2", [5, 6, 7, 8]);
        server.Body = failure switch
        {
            "truncated" => Gzip([5, 6]),
            "oversized" => Gzip(new byte[1024 * 1024]),
            "crc" => Gzip([5, 6, 7, 8]),
            _ => Gzip(original),
        };
        if (failure == "crc") server.Body[^8] ^= 0xff;
        if (failure == "unknown") server.Encoding = "br";

        var plan = await engine.AnalyzeAsync(root, v2);
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => engine.ApplyAsync(server.ManifestUri, root, plan));
        if (failure == "oversized") Assert.Contains("크기를 초과", exception.Message);
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(root, "Game.exe")));
        Assert.Equal("v1", (await LocalReleaseStore.ReadManifestAsync(root))?.Version);
        Assert.Empty(Directory.GetDirectories(Path.Combine(root, ".bettingroyale-patch")));
    }

    private static ReleaseManifest Manifest(string version, byte[] bytes) => new()
    {
        Product = "BettingRoyale", Version = version, Executable = "Game.exe",
        Files = [new ReleaseFile { Path = "Game.exe", Url = $"releases/{version}/Game.exe",
            Size = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) }],
    };

    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, true)) gzip.Write(bytes);
        return output.ToArray();
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private sealed class ReleaseServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly Task worker;
        public byte[] Body { get; set; } = [];
        public string? Encoding { get; set; }
        public string LastRequest { get; private set; } = string.Empty;
        public Uri ManifestUri { get; }

        public ReleaseServer()
        {
            listener.Start();
            ManifestUri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/game/manifest.json");
            worker = ServeAsync();
        }

        private async Task ServeAsync()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(stop.Token);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, leaveOpen: true);
                    var request = new StringBuilder();
                    while (await reader.ReadLineAsync(stop.Token) is { Length: > 0 } line) request.AppendLine(line);
                    LastRequest = request.ToString();
                    var body = Body;
                    var encoding = Encoding is null ? "" : $"Content-Encoding: {Encoding}\r\n";
                    var headers = $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\n{encoding}Connection: close\r\n\r\n";
                    await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(headers), stop.Token);
                    await stream.WriteAsync(body, stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (SocketException) when (stop.IsCancellationRequested) { }
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

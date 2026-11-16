using System.Net.Http.Headers;
using System.Text.Json;

namespace BettingRoyale.Launcher.Core;

public sealed class ManifestClient(HttpClient httpClient)
{
    /// <summary>지금 실행 중인 런처의 플랫폼(<see cref="ReleasePlatform.Current"/>) manifest 만 받아들인다.</summary>
    public Task<ReleaseManifest> GetAsync(
        Uri manifestUri,
        string expectedProduct,
        CancellationToken cancellationToken = default) =>
        GetAsync(manifestUri, expectedProduct, ReleasePlatform.Current, cancellationToken);

    public async Task<ReleaseManifest> GetAsync(
        Uri manifestUri,
        string expectedProduct,
        string expectedPlatform,
        CancellationToken cancellationToken = default)
    {
        try { return await GetManifestAsync(manifestUri, expectedProduct, expectedPlatform, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { LauncherDiagnostics.At(exception, "MANIFEST"); throw; }
    }

    private async Task<ReleaseManifest> GetManifestAsync(Uri manifestUri, string expectedProduct, string expectedPlatform,
        CancellationToken cancellationToken)
    {
        ValidateTransport(manifestUri);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        cancellationToken = timeout.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, manifestUri);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var manifest = await JsonSerializer.DeserializeAsync<ReleaseManifest>(
            stream,
            LauncherJsonContext.Default.ReleaseManifest,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("manifest JSON을 읽을 수 없습니다.");
        manifest.Validate();
        if (!manifest.Product.Equals(expectedProduct, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"manifest 제품이 올바르지 않습니다. expected={expectedProduct}, actual={manifest.Product}");
        }

        manifest.EnsurePlatform(expectedPlatform);
        return manifest;
    }

    /// <param name="allowedFolder">
    /// manifest 옆에서 파일이 있어도 되는 폴더. release 파일은 <c>releases</c>, 공유 런타임 설치기는 <c>runtime</c> 이다
    /// (D-REL-RW5QWY). 그 밖의 경로는 같은 서버라도 거부한다.
    /// </param>
    public static Uri ResolveFileUri(Uri manifestUri, ReleaseFile file, string allowedFolder = "releases")
    {
        file.Validate();
        var resolved = new Uri(manifestUri, file.Url);
        if (!string.Equals(resolved.Scheme, manifestUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(resolved.Authority, manifestUri.Authority, StringComparison.OrdinalIgnoreCase) ||
            !resolved.AbsolutePath.StartsWith(new Uri(manifestUri, $"./{allowedFolder}/").AbsolutePath, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"manifest와 다른 서버의 파일 URL은 허용되지 않습니다: {file.Path}");
        }

        ValidateTransport(resolved);
        return resolved;
    }

    public static void ValidateTransport(Uri uri)
    {
        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback)
        {
            return;
        }

        throw new InvalidDataException("배포 서버는 HTTPS여야 합니다. HTTP는 loopback 개발 서버만 허용됩니다.");
    }
}

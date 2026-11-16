using BettingRoyale.Launcher.Core;

namespace BettingRoyale.Launcher.Core.Tests;

public sealed class DeploymentEnvironmentTests
{
    [Theory]
    [InlineData("https://example.test/game/manifest.json", "production")]
    [InlineData("https://example.test/preview/game/manifest.json", "preview")]
    [InlineData("https://example.test/preview/launcher/manifest.json", "preview")]
    public void EnvironmentIsResolvedFromManifest(string url, string expected) =>
        Assert.Equal(expected, LauncherPaths.ResolveEnvironment(new Uri(url)));

    [Fact]
    public void LocalInstallRootsAreSeparate() =>
        Assert.NotEqual(LauncherPaths.ProductRootFor("preview"), LauncherPaths.ProductRootFor("production"));

    [Fact]
    public void FileCannotCrossFromPreviewToProduction()
    {
        var file = new ReleaseFile { Path = "game.exe", Url = "/game/releases/v1/game.exe", Size = 1, Sha256 = new string('a', 64) };
        Assert.Throws<InvalidDataException>(() => ManifestClient.ResolveFileUri(new Uri("https://example.test/preview/game/manifest.json"), file));
    }
}

using BettingRoyale.Launcher.Core;

namespace BettingRoyale.Launcher.Core.Tests;

public sealed class SafePathTests
{
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("folder/../../outside.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("folder//file.txt")]
    [InlineData("C:\\outside.txt")]
    [InlineData("data/file.txt:stream")]
    [InlineData("data/CON.txt")]
    [InlineData("data/trailing. ")]
    public void NormalizeRelativeRejectsPathsOutsideManagedRoot(string path)
    {
        Assert.Throws<InvalidDataException>(() => SafePath.NormalizeRelative(path));
    }

    [Fact]
    public void ResolveUnderRootKeepsNormalizedPathInsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "launcher-safe-path");
        var resolved = SafePath.ResolveUnderRoot(root, "data/game.bin");
        Assert.StartsWith(Path.GetFullPath(root), resolved, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("data", "game.bin"), resolved, StringComparison.OrdinalIgnoreCase);
    }
}

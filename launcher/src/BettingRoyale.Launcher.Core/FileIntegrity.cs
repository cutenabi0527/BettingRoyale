using System.Security.Cryptography;

namespace BettingRoyale.Launcher.Core;

public static class FileIntegrity
{
    public static async Task<bool> MatchesAsync(
        string path,
        ReleaseFile expected,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expected.Size)
        {
            return false;
        }

        var hash = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        return hash.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

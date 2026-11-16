using System.Text.Json;

namespace BettingRoyale.Launcher.Core;

public static class LocalReleaseStore
{
    public const string ManifestFileName = ".bettingroyale-manifest.json";
    public const string ManagedFileName = ".bettingroyale-managed.json";

    public static async Task<ReleaseManifest?> ReadManifestAsync(
        string root,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(root, ManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var manifest = await JsonSerializer.DeserializeAsync<ReleaseManifest>(
                stream,
                LauncherJsonContext.Default.ReleaseManifest,
                cancellationToken).ConfigureAwait(false);
            manifest?.Validate();
            return manifest;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    internal static async Task<IReadOnlyList<string>> ReadManagedFilesAsync(
        string root,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(root, ManagedFileName);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var state = await JsonSerializer.DeserializeAsync<ManagedFileState>(
                stream,
                LauncherJsonContext.Default.ManagedFileState,
                cancellationToken).ConfigureAwait(false);
            return state?.Files.Select(SafePath.NormalizeRelative).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (InvalidDataException)
        {
            return [];
        }
    }

    internal static async Task WriteAsync(
        string root,
        ReleaseManifest manifest,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, LauncherJsonContext.Default.ReleaseManifest);
        var managedBytes = JsonSerializer.SerializeToUtf8Bytes(
            new ManagedFileState { Files = manifest.Files.Select(file => SafePath.NormalizeRelative(file.Path)).ToArray() },
            LauncherJsonContext.Default.ManagedFileState);
        await WriteAtomicAsync(Path.Combine(root, ManifestFileName), manifestBytes, cancellationToken).ConfigureAwait(false);
        await WriteAtomicAsync(Path.Combine(root, ManagedFileName), managedBytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteAtomicAsync(string path, byte[] contents, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, contents, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, true);
    }
}

using System.Text.Json;

namespace BettingRoyale.Launcher.Core;

public sealed record LauncherRuntimeConfiguration
{
    public string DistributionChannel { get; init; } = "direct";
    public string GameManifestUrl { get; init; } = "https://bettingroyale.p.example.invalid/game/manifest.json";
    public string GameServerHealthUrl { get; init; } = "https://bettingroyale.p.example.invalid/health";
    public string? SteamAppId { get; init; }

    /// <summary>
    /// 런처 자체 갱신 manifest. Bootstrap 이 없는 macOS 런처만 쓴다(<c>launcher/macos/manifest.json</c>).
    /// Windows 런처는 Bootstrap 이 갱신하므로 비워 둔다.
    /// </summary>
    public string? LauncherManifestUrl { get; init; }

    /// <summary>
    /// 설정 파일이 있는 폴더. macOS <c>.app</c> 은 <c>Contents/MacOS</c> 에 Mach-O 만 두므로 설정은
    /// <c>Contents/Resources/launcher.runtime.json</c> 에 있다(S15P21A606-312). 그 밖(Windows·번들 밖 개발 실행)은
    /// 실행 파일 폴더 그대로다.
    /// </summary>
    public static string ResolveDirectory(string executableDirectory)
    {
        var macOs = new DirectoryInfo(Path.GetFullPath(executableDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (macOs.Name == "MacOS" && macOs.Parent is { Name: "Contents" } contents &&
            contents.Parent?.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) == true)
        {
            var resources = Path.Combine(contents.FullName, "Resources");
            if (File.Exists(Path.Combine(resources, "launcher.runtime.json"))) return resources;
        }

        return executableDirectory;
    }

    public static LauncherRuntimeConfiguration Load(string executableDirectory)
    {
        var path = Path.Combine(executableDirectory, "launcher.runtime.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("launcher.runtime.json is required; refusing to guess a deployment environment.", path);
        }

        var configuration = JsonSerializer.Deserialize<LauncherRuntimeConfiguration>(
            File.ReadAllText(path),
            LauncherJsonContext.Default.LauncherRuntimeConfiguration)
            ?? throw new InvalidDataException("launcher.runtime.json을 읽을 수 없습니다.");
        if (!configuration.DistributionChannel.Equals("direct", StringComparison.OrdinalIgnoreCase) &&
            !configuration.DistributionChannel.Equals("steam", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"지원하지 않는 배포 채널입니다: {configuration.DistributionChannel}");
        }

        if (configuration.DistributionChannel.Equals("direct", StringComparison.OrdinalIgnoreCase))
        {
            ManifestClient.ValidateTransport(new Uri(configuration.GameManifestUrl, UriKind.Absolute));
        }

        ManifestClient.ValidateTransport(new Uri(configuration.GameServerHealthUrl, UriKind.Absolute));
        if (!string.IsNullOrWhiteSpace(configuration.LauncherManifestUrl))
        {
            ManifestClient.ValidateTransport(new Uri(configuration.LauncherManifestUrl, UriKind.Absolute));
        }

        return configuration;
    }
}

public sealed record LauncherUserSettings
{
    public string InstallPath { get; init; } = LauncherPaths.DefaultGameRoot;
}

public static class LauncherPaths
{
    public static string EnvironmentKey { get; private set; } = "production";

    public static string ResolveEnvironment(Uri manifestUri) =>
        manifestUri.AbsolutePath.StartsWith("/preview/", StringComparison.OrdinalIgnoreCase)
            ? "preview" : "production";

    public static void Configure(Uri manifestUri) => EnvironmentKey = ResolveEnvironment(manifestUri);

    public static string ProductRootFor(string environment) => Path.Combine(
        LauncherPlatform.ApplicationDataRoot,
        "BettingRoyale", environment == "preview" ? "Preview" : "Production");

    public static string ProductRoot => ProductRootFor(EnvironmentKey);

    public static string LauncherMutexName => $"Local\\BettingRoyaleLauncher-{EnvironmentKey}";

    public static string DefaultGameRoot => Path.Combine(ProductRoot, LauncherPlatform.GameDirectoryName);
    public static string DefaultLauncherRoot => Path.Combine(ProductRoot, "Launcher", "current");
    public static string LogsRoot => Path.Combine(ProductRoot, "Logs");
    public static string SettingsPath => Path.Combine(ProductRoot, "launcher-settings.json");

    public static void ValidateInstallPath(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var other = ProductRootFor(EnvironmentKey == "preview" ? "production" : "preview") + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(other, StringComparison.OrdinalIgnoreCase) ||
            other.StartsWith(fullPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("다른 환경의 설치 폴더와 겹치는 경로는 사용할 수 없습니다.");
        var marker = Path.Combine(path, ".bettingroyale-environment");
        if (File.Exists(marker) && File.ReadAllText(marker).Trim() != EnvironmentKey)
            throw new InvalidOperationException("다른 환경이 사용하는 설치 폴더입니다.");
    }

    public static void ClaimInstallPath(string path)
    {
        ValidateInstallPath(path);
        Directory.CreateDirectory(path);
        var marker = Path.Combine(path, ".bettingroyale-environment");
        if (!File.Exists(marker))
        {
            using var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(EnvironmentKey);
        }
    }
}

public static class LauncherUserSettingsStore
{
    public static LauncherUserSettings Load()
    {
        if (!File.Exists(LauncherPaths.SettingsPath))
        {
            return new LauncherUserSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<LauncherUserSettings>(
                File.ReadAllText(LauncherPaths.SettingsPath),
                LauncherJsonContext.Default.LauncherUserSettings);
            return settings is null || string.IsNullOrWhiteSpace(settings.InstallPath)
                ? new LauncherUserSettings()
                : settings;
        }
        catch (JsonException)
        {
            return new LauncherUserSettings();
        }
    }

    public static async Task SaveAsync(LauncherUserSettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.InstallPath))
        {
            throw new ArgumentException("설치 경로가 비어 있습니다.", nameof(settings));
        }

        Directory.CreateDirectory(LauncherPaths.ProductRoot);
        var temporary = LauncherPaths.SettingsPath + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, LauncherJsonContext.Default.LauncherUserSettings);
        await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, LauncherPaths.SettingsPath, true);
    }
}

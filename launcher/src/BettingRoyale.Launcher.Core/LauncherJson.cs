using System.Text.Json;
using System.Text.Json.Serialization;

namespace BettingRoyale.Launcher.Core;

public static class LauncherJson
{
    public static JsonSerializerOptions Options => LauncherJsonContext.Default.Options;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true, WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ReleaseManifest))]
[JsonSerializable(typeof(RuntimeRequirement))]
[JsonSerializable(typeof(ManagedFileState))]
[JsonSerializable(typeof(LauncherRuntimeConfiguration))]
[JsonSerializable(typeof(LauncherUserSettings))]
internal partial class LauncherJsonContext : JsonSerializerContext;

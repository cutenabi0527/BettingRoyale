using System.Text.Json.Serialization;

namespace BettingRoyale.Launcher.Core;

public sealed record ReleaseManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string Product { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Executable { get; init; } = string.Empty;
    public string? MinimumBootstrapVersion { get; init; }
    public string? MinimumLauncherVersion { get; init; }
    public string Channel { get; init; } = "direct";

    /// <summary>
    /// 이 release 가 설치될 운영체제·아키텍처(<see cref="ReleasePlatform"/>). 생략하면 <c>windows-x64</c> 다 —
    /// 필드가 생기기 전의 Windows manifest 와 구 런처가 그대로 동작한다(S15P21A606-312).
    /// </summary>
    public string? Platform { get; init; }

    [JsonIgnore]
    public string EffectivePlatform => string.IsNullOrWhiteSpace(Platform) ? ReleasePlatform.WindowsX64 : Platform;
    public DateTimeOffset PublishedAt { get; init; }
    public IReadOnlyList<string> PatchNotes { get; init; } = [];
    public IReadOnlyList<ReleaseFile> Files { get; init; } = [];

    /// <summary>
    /// 이 release 를 실행하기 위해 PC 에 있어야 하는 공유 런타임. 없으면 Bootstrap 이 설치한다 (D-REL-RW5QWY).
    /// schema 1 의 선택 필드다 — 모르는 구 Bootstrap 은 무시하고, 그 경우 framework-dependent 런처는
    /// 런타임이 없는 PC 에서 시작하지 못한다. 그래서 Bootstrap 을 먼저 배포한다.
    /// </summary>
    public RuntimeRequirement? Runtime { get; init; }

    [JsonIgnore]
    public long TotalSize => Files.Sum(file => file.Size);

    public void Validate()
    {
        if (SchemaVersion != 1)
        {
            throw new InvalidDataException($"지원하지 않는 manifest schema입니다: {SchemaVersion}");
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            throw new InvalidDataException("manifest version이 비어 있습니다.");
        }

        if (string.IsNullOrWhiteSpace(Product))
        {
            throw new InvalidDataException("manifest product가 비어 있습니다.");
        }

        if (string.IsNullOrWhiteSpace(Executable))
        {
            throw new InvalidDataException("manifest executable이 비어 있습니다.");
        }

        _ = SafePath.NormalizeRelative(Executable);

        if (!ReleasePlatform.IsKnown(EffectivePlatform))
        {
            throw new InvalidDataException($"지원하지 않는 manifest platform입니다: {Platform}");
        }

        if (Files.Count == 0)
        {
            throw new InvalidDataException("manifest에 파일이 없습니다.");
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Files)
        {
            file.Validate();
            var normalized = SafePath.NormalizeRelative(file.Path);
            if (!paths.Add(normalized))
            {
                throw new InvalidDataException($"manifest에 중복 경로가 있습니다: {normalized}");
            }
        }

        if (!paths.Contains(SafePath.NormalizeRelative(Executable)))
        {
            throw new InvalidDataException($"실행 파일이 manifest 파일 목록에 없습니다: {Executable}");
        }

        if (ReleasePlatform.UsesUnixFileModes(EffectivePlatform))
        {
            // Unix 는 실행 권한이 없으면 실행 파일을 시작할 수 없다. 게시 단계의 누락을 설치 전에 막는다.
            var main = SafePath.NormalizeRelative(Executable);
            if (!Files.Any(file => file.Executable == true &&
                    string.Equals(SafePath.NormalizeRelative(file.Path), main, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException($"실행 파일에 executable 표시가 없습니다: {Executable}");
            }
        }

        if (Runtime is not null && EffectivePlatform != ReleasePlatform.WindowsX64)
        {
            // .NET Desktop Runtime 설치기(runtime/*.exe)는 Windows 전용이다 (D-REL-RW5QWY).
            throw new InvalidDataException($"공유 런타임 설치는 {ReleasePlatform.WindowsX64} manifest 에서만 허용됩니다.");
        }

        Runtime?.Validate();
    }

    /// <summary>이 런처가 설치·실행할 수 있는 플랫폼인지 확인한다. 다른 플랫폼 manifest 는 파일을 받기 전에 거부한다.</summary>
    public void EnsurePlatform(string expectedPlatform)
    {
        if (!string.Equals(EffectivePlatform, expectedPlatform, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"다른 플랫폼용 manifest 입니다. expected={expectedPlatform}, actual={EffectivePlatform}");
        }
    }
}

/// <summary>공유 런타임 설치 파일. 같은 서버의 상대 URL 이며 release 파일 목록과는 별개로 검증한다.</summary>
public sealed record RuntimeRequirement
{
    public const string WindowsDesktop = "Microsoft.WindowsDesktop.App";

    public string Name { get; init; } = WindowsDesktop;
    public string Version { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public long Size { get; init; }
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>다운로드·검증에 release 파일과 같은 경로를 쓰기 위한 변환.</summary>
    public ReleaseFile AsReleaseFile()
    {
        var fileName = Url.Split('/')[^1];
        return new ReleaseFile { Path = Uri.UnescapeDataString(fileName), Url = Url, Size = Size, Sha256 = Sha256 };
    }

    public System.Version ParsedVersion => System.Version.Parse(Version);

    public void Validate()
    {
        if (Name != WindowsDesktop)
        {
            throw new InvalidDataException($"지원하지 않는 런타임입니다: {Name}");
        }

        if (!System.Version.TryParse(Version, out var parsed) || parsed.Major <= 0 || parsed.Build < 0)
        {
            throw new InvalidDataException($"런타임 버전 형식이 올바르지 않습니다: {Version}");
        }

        if (Size <= 0)
        {
            throw new InvalidDataException("런타임 설치 파일 크기가 올바르지 않습니다.");
        }

        var file = AsReleaseFile();
        file.Validate();
        var segments = Url.Split('/');
        if (segments.Length != 2 || segments[0] != "runtime" || !file.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"런타임 설치 파일 URL 은 runtime/<설치기>.exe 여야 합니다: {Url}");
        }
    }
}

/// <summary>manifest <c>platform</c> 값. 런처는 자기 플랫폼과 다른 manifest 를 거부한다.</summary>
public static class ReleasePlatform
{
    public const string WindowsX64 = "windows-x64";
    public const string MacOSArm64 = "macos-arm64";

    /// <summary>
    /// 지금 실행 중인 런처의 플랫폼. macOS 가 아니면 Windows 로 본다 — Linux 는 배포 대상이 아니며
    /// CI 의 Linux 테스트가 기존 Windows manifest 를 그대로 검증할 수 있게 한다.
    /// </summary>
    public static string Current => OperatingSystem.IsMacOS() ? MacOSArm64 : WindowsX64;

    public static bool IsKnown(string platform) => platform is WindowsX64 or MacOSArm64;

    public static bool UsesUnixFileModes(string platform) => platform == MacOSArm64;
}

public sealed record ReleaseFile
{
    public string Path { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public long Size { get; init; }
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>
    /// Unix 플랫폼에서 설치 후 실행 권한(0755)을 줄 파일인지. 생략·false 면 0644 다. Windows 에서는 무시한다.
    /// null 이면 JSON 에 쓰지 않아 기존 Windows manifest 모양이 바뀌지 않는다.
    /// </summary>
    public bool? Executable { get; init; }

    public void Validate()
    {
        _ = SafePath.NormalizeRelative(Path);
        if (Size < 0)
        {
            throw new InvalidDataException($"파일 크기가 올바르지 않습니다: {Path}");
        }

        if (Sha256.Length != 64 || Sha256.Any(ch => !Uri.IsHexDigit(ch)))
        {
            throw new InvalidDataException($"SHA-256 형식이 올바르지 않습니다: {Path}");
        }

        if (Uri.TryCreate(Url, UriKind.Absolute, out _) ||
            !Uri.TryCreate(Url, UriKind.Relative, out _) ||
            Url.StartsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"파일 URL은 같은 서버의 상대 경로여야 합니다: {Path}");
        }
    }
}

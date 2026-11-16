namespace BettingRoyale.Launcher.Core;

public static class SafePath
{
    private static readonly char[] WindowsInvalidCharacters = ['<', '>', ':', '"', '|', '?', '*'];
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string NormalizeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException("빈 파일 경로는 허용되지 않습니다.");
        }

        var normalized = path.Replace('\\', '/').Trim();
        var parts = normalized.Split('/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized) ||
            (normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':') ||
            parts.Any(IsUnsafeWindowsPart))
        {
            throw new InvalidDataException($"설치 루트 밖을 가리키는 경로입니다: {path}");
        }

        return normalized;
    }

    private static bool IsUnsafeWindowsPart(string part)
    {
        if (part is "" or "." or ".." ||
            part.EndsWith(' ') ||
            part.EndsWith('.') ||
            part.Any(ch => ch < 32 || WindowsInvalidCharacters.Contains(ch)))
        {
            return true;
        }

        var deviceName = part.Split('.')[0];
        return WindowsReservedNames.Contains(deviceName);
    }

    public static string ResolveUnderRoot(string root, string relativePath)
    {
        var normalized = NormalizeRelative(relativePath);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"설치 루트 밖을 가리키는 경로입니다: {relativePath}");
        }

        return candidate;
    }
}

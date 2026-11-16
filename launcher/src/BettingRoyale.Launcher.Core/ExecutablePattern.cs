using System.Text;
using System.Text.RegularExpressions;

namespace BettingRoyale.Launcher.Core;

/// <summary>
/// ManifestTool <c>--executable</c> 패턴. manifest 상대 경로(<c>/</c> 구분)에 대해
/// <c>*</c>(한 경로 조각 안), <c>**</c>(여러 조각), <c>?</c>(한 글자)를 지원한다. 대소문자는 구분하지 않는다.
/// </summary>
public static class ExecutablePattern
{
    /// <summary>macOS manifest 기본 규칙: 모든 <c>.app</c> 번들의 <c>Contents/MacOS</c> 바로 아래 파일은 실행 파일이다.</summary>
    public const string MacBundleDefault = "*.app/Contents/MacOS/*";

    public static bool HasWildcard(string value) => value.IndexOfAny(['*', '?']) >= 0;

    public static string Normalize(string pattern)
    {
        var normalized = pattern.Replace('\\', '/').Trim().TrimStart('/');
        if (normalized.Length == 0 || normalized.Split('/').Any(part => part is "." or ".."))
        {
            throw new ArgumentException($"실행 파일 패턴이 올바르지 않습니다: {pattern}");
        }

        return normalized;
    }

    public static bool Matches(string pattern, string relativePath) =>
        ToRegex(Normalize(pattern)).IsMatch(SafePath.NormalizeRelative(relativePath));

    private static Regex ToRegex(string pattern)
    {
        var builder = new StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            var ch = pattern[index];
            if (ch == '*' && index + 1 < pattern.Length && pattern[index + 1] == '*')
            {
                builder.Append(".*");
                index++;
            }
            else if (ch == '*')
            {
                builder.Append("[^/]*");
            }
            else if (ch == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(ch.ToString()));
            }
        }

        return new Regex(builder.Append('$').ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}

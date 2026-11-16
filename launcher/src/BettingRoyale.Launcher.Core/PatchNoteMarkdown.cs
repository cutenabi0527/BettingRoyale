using System.Text.RegularExpressions;

namespace BettingRoyale.Launcher.Core;

public enum MarkdownBlockKind { Heading, Paragraph, ListItem, Rule }

/// <summary>인라인 조각. 굵게·기울임·코드·링크를 조합할 수 있다.</summary>
public sealed record MarkdownInline(string Text, bool Bold = false, bool Italic = false, bool Code = false, string? Link = null);

/// <summary><paramref name="Marker"/> 는 목록 항목의 앞머리("•" 또는 "1.")다. <paramref name="Level"/> 은 제목 단계(1~3)다.</summary>
public sealed record MarkdownBlock(MarkdownBlockKind Kind, int Level, string Marker, IReadOnlyList<MarkdownInline> Inlines)
{
    public string PlainText => string.Concat(Inlines.Select(inline => inline.Text));
}

/// <summary>
/// manifest patchNotes(줄 단위 Markdown)를 런처가 그릴 수 있는 블록으로 해석한다 (S-REL-5ZT5AW).
/// 지원: <c>#</c>~<c>###</c> 제목, <c>-</c>/<c>*</c>/<c>1.</c> 목록, <c>---</c> 구분선, <c>**굵게**</c>, <c>*기울임*</c>,
/// <c>`코드`</c>, <c>[텍스트](https://…)</c>. 그 밖은 문장 그대로다. 렌더링은 Launcher 프로젝트가 맡는다.
/// </summary>
public static partial class PatchNoteMarkdown
{
    public const string EmptyMessage = "이번 버전의 패치노트가 없습니다.";

    public static IReadOnlyList<MarkdownBlock> Parse(IReadOnlyList<string> lines)
    {
        var blocks = new List<MarkdownBlock>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (RuleLine().IsMatch(line))
            {
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Rule, 0, string.Empty, []));
                continue;
            }

            var heading = HeadingLine().Match(line);
            if (heading.Success)
            {
                var text = heading.Groups[2].Value.Trim();
                if (text.Length > 0)
                    blocks.Add(new MarkdownBlock(MarkdownBlockKind.Heading, Math.Min(heading.Groups[1].Value.Length, 3), string.Empty, ParseInlines(text)));
                continue;
            }

            var bullet = BulletLine().Match(line);
            if (bullet.Success)
            {
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.ListItem, 0, "•", ParseInlines(bullet.Groups[1].Value)));
                continue;
            }

            var ordered = OrderedLine().Match(line);
            if (ordered.Success)
            {
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.ListItem, 0, ordered.Groups[1].Value + ".", ParseInlines(ordered.Groups[2].Value)));
                continue;
            }

            blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph, 0, string.Empty, ParseInlines(line)));
        }

        return blocks;
    }

    /// <summary>접근성·로그용 평문. 제목은 그대로, 목록은 앞머리를 붙인다.</summary>
    public static string ToPlainText(IReadOnlyList<MarkdownBlock> blocks)
    {
        if (blocks.Count == 0) return EmptyMessage;
        return string.Join(Environment.NewLine, blocks.Select(block => block.Kind switch
        {
            MarkdownBlockKind.Rule => "――――",
            MarkdownBlockKind.ListItem => block.Marker + " " + block.PlainText,
            _ => block.PlainText,
        }));
    }

    public static IReadOnlyList<MarkdownInline> ParseInlines(string text)
    {
        var result = new List<MarkdownInline>();
        var index = 0;
        foreach (Match match in InlineToken().Matches(text))
        {
            if (match.Index > index) result.Add(new MarkdownInline(text[index..match.Index]));
            if (match.Groups["code"].Success) result.Add(new MarkdownInline(match.Groups["code"].Value, Code: true));
            else if (match.Groups["bold"].Success) result.Add(new MarkdownInline(match.Groups["bold"].Value, Bold: true));
            else if (match.Groups["italic"].Success) result.Add(new MarkdownInline(match.Groups["italic"].Value, Italic: true));
            else if (match.Groups["ltext"].Success)
            {
                var url = match.Groups["lurl"].Value;
                var safe = Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                           (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
                // http(s) 가 아닌 링크는 열지 않고 글자만 남긴다 — file:// 나 실행 스킴이 패치노트로 들어오는 것을 막는다.
                result.Add(new MarkdownInline(match.Groups["ltext"].Value, Link: safe ? url : null));
            }

            index = match.Index + match.Length;
        }

        if (index < text.Length) result.Add(new MarkdownInline(text[index..]));
        return result;
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")] private static partial Regex HeadingLine();
    [GeneratedRegex(@"^[-*]\s+(.+)$")] private static partial Regex BulletLine();
    [GeneratedRegex(@"^(\d{1,3})\.\s+(.+)$")] private static partial Regex OrderedLine();
    [GeneratedRegex(@"^(-{3,}|\*{3,}|_{3,})$")] private static partial Regex RuleLine();
    [GeneratedRegex(@"`(?<code>[^`]+)`|\*\*(?<bold>[^*]+)\*\*|\*(?<italic>[^*]+)\*|\[(?<ltext>[^\]]+)\]\((?<lurl>[^)\s]+)\)")]
    private static partial Regex InlineToken();
}

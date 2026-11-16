using BettingRoyale.Launcher.Core;

namespace BettingRoyale.Launcher.Core.Tests;

public sealed class PatchNoteMarkdownTests
{
    [Fact]
    public void ParsesHeadingsListsRulesAndParagraphs()
    {
        var blocks = PatchNoteMarkdown.Parse(["# 용량 최적화", "- 설치 용량을 **절반**으로", "* 두 번째", "2. 순서 있는 항목", "---", "그냥 문장", "  ", "####### 너무 깊음"]);
        Assert.Equal(MarkdownBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(1, blocks[0].Level);
        Assert.Equal("용량 최적화", blocks[0].PlainText);
        Assert.Equal(MarkdownBlockKind.ListItem, blocks[1].Kind);
        Assert.Equal("•", blocks[1].Marker);
        Assert.Contains(blocks[1].Inlines, inline => inline.Bold && inline.Text == "절반");
        Assert.Equal("2.", blocks[3].Marker);
        Assert.Equal(MarkdownBlockKind.Rule, blocks[4].Kind);
        Assert.Equal(MarkdownBlockKind.Paragraph, blocks[5].Kind);
        Assert.Equal(MarkdownBlockKind.Paragraph, blocks[6].Kind); // 7개 '#' 은 제목이 아니라 문장으로 남는다
        Assert.Equal(7, blocks.Count);
    }

    [Fact]
    public void InlineCodeItalicAndSafeLinksOnly()
    {
        var inlines = PatchNoteMarkdown.ParseInlines("`code` *it* [문서](https://example.com/a) [위험](file:///c:/x) 끝");
        Assert.Collection(inlines,
            i => Assert.True(i.Code && i.Text == "code"),
            i => Assert.Equal(" ", i.Text),
            i => Assert.True(i.Italic && i.Text == "it"),
            i => Assert.Equal(" ", i.Text),
            i => Assert.Equal("https://example.com/a", i.Link),
            i => Assert.Equal(" ", i.Text),
            i => Assert.True(i.Text == "위험" && i.Link is null),
            i => Assert.Equal(" 끝", i.Text));
    }

    [Fact]
    public void PlainTextFallbacks()
    {
        Assert.Equal(PatchNoteMarkdown.EmptyMessage, PatchNoteMarkdown.ToPlainText(PatchNoteMarkdown.Parse([])));
        var text = PatchNoteMarkdown.ToPlainText(PatchNoteMarkdown.Parse(["## 제목", "- 항목", "---"]));
        Assert.Equal(string.Join(Environment.NewLine, ["제목", "• 항목", "――――"]), text);
    }
}

using System.Linq;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;
using Xunit;

public class MarkdownParserTests
{
    [Fact]
    public void Headings_paragraphs_and_rules_parse()
    {
        var blocks = MarkdownParser.Parse("# Title\n\nSome intro text\nsame paragraph.\n\n---\n\n## Usage");
        Assert.Collection(blocks,
            b => Assert.Equal(1, Assert.IsType<MdHeading>(b).Level),
            b => Assert.Equal("Some intro text same paragraph.",
                string.Concat(Assert.IsType<MdParagraph>(b).Inlines.Cast<MdText>().Select(t => t.Text))),
            b => Assert.IsType<MdRule>(b),
            b => Assert.Equal(2, Assert.IsType<MdHeading>(b).Level));
    }

    [Fact]
    public void Fenced_code_block_preserves_lines_verbatim()
    {
        var blocks = MarkdownParser.Parse("```ini\n[Section]\nKey = *not italic*\n```");
        var code = Assert.IsType<MdCodeBlock>(Assert.Single(blocks));
        Assert.Equal("[Section]\nKey = *not italic*", code.Text);
    }

    [Fact]
    public void Unterminated_fence_consumes_to_end_without_throwing()
    {
        var blocks = MarkdownParser.Parse("```\nno closing fence");
        Assert.Equal("no closing fence", Assert.IsType<MdCodeBlock>(Assert.Single(blocks)).Text);
    }

    [Fact]
    public void Bullet_and_ordered_lists_parse_with_markers_and_nesting()
    {
        var blocks = MarkdownParser.Parse("- top\n  - nested\n1. first\n2) second");
        var list = Assert.IsType<MdList>(Assert.Single(blocks));
        Assert.Equal(4, list.Items.Count);
        Assert.Equal(("•", 0), (list.Items[0].Marker, list.Items[0].Indent));
        Assert.Equal(("◦", 1), (list.Items[1].Marker, list.Items[1].Indent));
        Assert.Equal("1.", list.Items[2].Marker);
        Assert.Equal("2.", list.Items[3].Marker);
    }

    [Fact]
    public void List_item_continuation_lines_join_their_item()
    {
        var blocks = MarkdownParser.Parse("- first part\n  continues here\n- second item\nlazy continuation\n\nreal paragraph");
        Assert.Equal(2, blocks.Count);
        var list = Assert.IsType<MdList>(blocks[0]);
        Assert.Equal(2, list.Items.Count);
        Assert.Equal("first part continues here",
            string.Concat(list.Items[0].Inlines.Cast<MdText>().Select(t => t.Text)));
        Assert.Equal("second item lazy continuation",
            string.Concat(list.Items[1].Inlines.Cast<MdText>().Select(t => t.Text)));
        Assert.IsType<MdParagraph>(blocks[1]);
    }

    [Fact]
    public void Quotes_join_consecutive_lines()
    {
        var q = Assert.IsType<MdQuote>(Assert.Single(MarkdownParser.Parse("> one\n> two")));
        Assert.Equal("one two", Assert.IsType<MdText>(Assert.Single(q.Inlines)).Text);
    }

    [Fact]
    public void Standalone_image_becomes_block_image()
    {
        var img = Assert.IsType<MdImage>(Assert.Single(MarkdownParser.Parse("![The meter](https://x/y.png)")));
        Assert.Equal(("https://x/y.png", "The meter"), (img.Url, img.Alt));
    }

    [Fact]
    public void Inline_bold_italic_code_and_links_parse()
    {
        var inlines = MarkdownParser.ParseInlines("mix **bold** and *ital* and `code` and [docs](https://d)");
        Assert.Collection(inlines,
            x => Assert.Equal("mix ", Assert.IsType<MdText>(x).Text),
            x => Assert.True(Assert.IsType<MdText>(x) is { Text: "bold", Bold: true }),
            x => Assert.Equal(" and ", Assert.IsType<MdText>(x).Text),
            x => Assert.True(Assert.IsType<MdText>(x) is { Text: "ital", Italic: true }),
            x => Assert.Equal(" and ", Assert.IsType<MdText>(x).Text),
            x => Assert.True(Assert.IsType<MdText>(x) is { Text: "code", Code: true }),
            x => Assert.Equal(" and ", Assert.IsType<MdText>(x).Text),
            x => Assert.Equal(("docs", "https://d"), Assert.IsType<MdLink>(x) is var l ? (l.Text, l.Url) : default));
    }

    [Fact]
    public void Unterminated_markers_stay_literal()
    {
        var only = Assert.IsType<MdText>(Assert.Single(MarkdownParser.ParseInlines("a *dangling star and `tick")));
        Assert.Equal("a *dangling star and `tick", only.Text);
    }

    [Fact]
    public void Raw_html_is_kept_as_literal_text()
    {
        var p = Assert.IsType<MdParagraph>(Assert.Single(MarkdownParser.Parse("<script>alert(1)</script>")));
        Assert.Equal("<script>alert(1)</script>", Assert.IsType<MdText>(Assert.Single(p.Inlines)).Text);
    }

    [Fact]
    public void Empty_input_yields_no_blocks()
    {
        Assert.Empty(MarkdownParser.Parse(""));
        Assert.Empty(MarkdownParser.Parse("\n  \n"));
    }
}

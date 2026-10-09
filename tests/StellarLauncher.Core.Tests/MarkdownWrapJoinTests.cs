using System.Linq;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;
using Xunit;

// Launcher i18n: a soft-wrapped guide line must not insert a visible space in scripts written without spaces.
public class MarkdownWrapJoinTests
{
    private static string Para(string md) =>
        string.Concat(Assert.IsType<MdParagraph>(Assert.Single(MarkdownParser.Parse(md))).Inlines.Cast<MdText>().Select(t => t.Text));

    private static string Item(string md) =>
        string.Concat(Assert.IsType<MdList>(Assert.Single(MarkdownParser.Parse(md))).Items[0].Inlines.Cast<MdText>().Select(t => t.Text));

    [Theory]
    [InlineData("フォトスタジオは撮影用の\nプラグインです。", "フォトスタジオは撮影用のプラグインです。")]       // ja: kana ↔ kana
    [InlineData("設定を開き、\n「保存」を押します。", "設定を開き、「保存」を押します。")]                 // ja: punctuation ↔ punctuation
    [InlineData("ปลั๊กอินนี้ช่วย\nถ่ายภาพ", "ปลั๊กอินนี้ช่วยถ่ายภาพ")]                                   // th
    [InlineData("포토 스튜디오는 촬영용\n플러그인입니다.", "포토 스튜디오는 촬영용 플러그인입니다.")]         // ko: Hangul keeps the space
    [InlineData("Some intro text\nsame paragraph.", "Some intro text same paragraph.")]                     // en
    [InlineData("Photo Studio は\n撮影用です", "Photo Studio は撮影用です")]                               // ja after Latin word
    [InlineData("撮影には\nReShade を使います", "撮影には ReShade を使います")]                           // CJK ↔ Latin keeps a space
    public void Paragraph_soft_wraps_join_per_script(string md, string expected) => Assert.Equal(expected, Para(md));

    [Fact]
    public void List_item_continuations_follow_the_same_rule()
    {
        Assert.Equal("一行目の続きは二行目", Item("- 一行目の続きは\n  二行目"));
        Assert.Equal("first line continues here", Item("- first line\n  continues here"));
        Assert.Equal("첫 줄이 다음 줄로", Item("- 첫 줄이\n  다음 줄로"));
    }
}

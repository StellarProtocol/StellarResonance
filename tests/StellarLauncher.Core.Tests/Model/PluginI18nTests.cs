using System.Text.Json;
using StellarLauncher.Core.Model;
using Xunit;

// Launcher i18n Task 4: the registry's additive per-language presentation (plugins repo CONTRIBUTING § Translations,
// build-registry.py) — entry guideUrls + i18n{name,description,captions}, version changelogI18n — read with PER-FIELD
// English fallback, and never able to break the registry parse.
public class PluginI18nTests
{
    private const string Registry = """
    { "plugins": [
      { "id": "photostudio", "name": "Photo Studio", "description": "Take beautiful screenshots.", "author": "StellarProtocol",
        "guideUrl": "https://cdn/plugins/photostudio/guide.md",
        "guideUrls": { "ja": "https://cdn/plugins/photostudio/guide.ja.md", "ko": "https://cdn/plugins/photostudio/guide.ko.md" },
        "media": [ { "type": "image", "url": "https://cdn/a.png", "caption": "Panel next to a boss" },
                   { "type": "image", "url": "https://cdn/b.png", "caption": "Group photo" },
                   { "type": "image", "url": "https://cdn/c.png" } ],
        "i18n": {
          "ja": { "name": "フォトスタジオ", "description": "美しいスクリーンショットを撮ろう。", "captions": ["ボスの隣のパネル", null] },
          "ko": { "description": "멋진 스크린샷을 찍으세요." },
          "th": { "name": "", "captions": "not-a-list" }
        },
        "versions": [ { "version": "1.7.0", "dllUrl": "https://cdn/ps.dll", "sha256": "x", "minModSystemVersion": "2.0.0",
                        "changelog": { "added": ["Korean support"], "changed": [], "fixed": ["Camera jitter"], "removed": [] },
                        "changelogI18n": { "ja": { "added": ["韓国語に対応"] }, "ko": "oops" } } ] },
      { "id": "plain", "name": "Plain", "description": "No translations.", "author": null,
        "versions": [ { "version": "1.0.0", "dllUrl": "https://cdn/p.dll", "sha256": "y", "minModSystemVersion": "2.0.0",
                        "changelog": { "added": ["First"], "changed": [], "fixed": [], "removed": [] } } ] },
      { "id": "weird", "name": "Weird", "description": "Malformed i18n everywhere.", "author": null,
        "guideUrl": "https://cdn/w/guide.md", "guideUrls": ["nope"], "i18n": 42,
        "versions": [ { "version": "1.0.0", "dllUrl": "https://cdn/w.dll", "sha256": "z", "minModSystemVersion": "2.0.0",
                        "changelog": null, "changelogI18n": { "ja": { "added": "x" } } } ] }
    ] }
    """;

    private static PluginEntry Entry(string id) =>
        JsonSerializer.Deserialize<PluginRegistry>(Registry, PluginRegistry.JsonOptions)!.Plugins.Single(p => p.Id == id);

    [Fact]
    public void Name_and_description_pick_the_language_with_per_field_english_fallback()
    {
        var e = Entry("photostudio");
        Assert.Equal("フォトスタジオ", e.DisplayName("ja"));
        Assert.Equal("美しいスクリーンショットを撮ろう。", e.DisplayDescription("ja"));
        Assert.Equal("Photo Studio", e.DisplayName("ko"));                         // ko translated only the description
        Assert.Equal("멋진 스크린샷을 찍으세요.", e.DisplayDescription("ko"));
        Assert.Equal("Photo Studio", e.DisplayName("th"));                          // empty name → English
        Assert.Equal("Take beautiful screenshots.", e.DisplayDescription("id"));    // no block → English
        Assert.Equal("Photo Studio", e.DisplayName("en"));
        Assert.Equal("Photo Studio", e.DisplayName("xx"));                          // unsupported code → English
    }

    [Fact]
    public void Captions_translate_by_index_and_fall_back_per_item()
    {
        var e = Entry("photostudio");
        Assert.Equal("ボスの隣のパネル", e.MediaCaption(0, "ja"));
        Assert.Equal("Group photo", e.MediaCaption(1, "ja"));    // null = untranslated
        Assert.Null(e.MediaCaption(2, "ja"));                    // no English caption → nothing (and the list is shorter)
        Assert.Equal("Panel next to a boss", e.MediaCaption(0, "th"));   // malformed captions → English
        Assert.Equal("Panel next to a boss", e.MediaCaption(0, "en"));
        Assert.Null(e.MediaCaption(9, "ja"));                    // out of range never throws
    }

    [Fact]
    public void Guide_url_uses_the_language_when_published_else_english()
    {
        var e = Entry("photostudio");
        Assert.Equal("https://cdn/plugins/photostudio/guide.ja.md", e.GuideUrlFor("ja"));
        Assert.Equal("https://cdn/plugins/photostudio/guide.ko.md", e.GuideUrlFor("ko"));
        Assert.Equal("https://cdn/plugins/photostudio/guide.md", e.GuideUrlFor("th"));   // missing language → English
        Assert.Equal("https://cdn/plugins/photostudio/guide.md", e.GuideUrlFor("en"));
        Assert.Null(Entry("plain").GuideUrlFor("ja"));
    }

    [Fact]
    public void Changelog_falls_back_section_by_section()
    {
        var v = Entry("photostudio").Versions[0];
        var ja = v.ChangelogFor("ja")!;
        Assert.Equal(new[] { "韓国語に対応" }, ja.Added);
        Assert.Equal(new[] { "Camera jitter" }, ja.Fixed);        // untranslated section stays English
        Assert.Empty(ja.Changed);
        Assert.Equal(new[] { "Korean support" }, v.ChangelogFor("ko")!.Added);   // malformed block → English
        Assert.Same(v.Changelog, v.ChangelogFor("en"));
    }

    [Fact]
    public void A_registry_without_i18n_reads_exactly_as_before()
    {
        var e = Entry("plain");
        Assert.Null(e.I18n); Assert.Null(e.GuideUrls); Assert.Null(e.Versions[0].ChangelogI18n);
        Assert.Equal("Plain", e.DisplayName("ja"));
        Assert.Equal("No translations.", e.DisplayDescription("ko"));
        Assert.Equal(new[] { "First" }, e.Versions[0].ChangelogFor("ja")!.Added);
    }

    [Fact]
    public void Malformed_i18n_never_breaks_the_parse_and_reads_as_english()
    {
        var e = Entry("weird");   // i18n: 42, guideUrls: [..], changelogI18n with a non-list section
        Assert.Equal("Weird", e.DisplayName("ja"));
        Assert.Equal("https://cdn/w/guide.md", e.GuideUrlFor("ja"));
        Assert.Null(e.Versions[0].ChangelogFor("ja"));            // no English changelog → none
    }
}

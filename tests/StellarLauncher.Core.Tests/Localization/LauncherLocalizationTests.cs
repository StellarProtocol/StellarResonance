using System.IO.Abstractions.TestingHelpers;
using System.Globalization;
using System.Text.Json;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Localization;
using StellarLauncher.Core.Platform;
using Xunit;

public class LauncherLocalizationTests
{
    private static readonly Dictionary<string, string> Catalogs = new()
    {
        ["en"] = """{ "a": "Hello", "only.en": "English only", "fmt": "{0} of {1}", "bad": "{0" }""",
        ["ja"] = """{ "a": "こんにちは", "fmt": "{1}件中{0}件" }""",
        ["ko"] = """{ "a": "안녕하세요" }""",
        ["th"] = "not json",   // a broken catalog must degrade to English, never throw
    };

    private static LauncherLocalization Make(string? setting, string os = "en") => new(setting, () => os, Catalogs);

    [Fact]
    public void Languages_are_the_six_in_dropdown_order_with_native_names()
    {
        Assert.Equal(new[] { "en", "ja", "th", "id", "fil", "ko" }, LauncherLanguages.Codes);
        Assert.Equal(new[] { "English", "日本語", "ไทย", "Bahasa Indonesia", "Filipino", "한국어" }, LauncherLanguages.NativeNames);
    }

    // Real CultureInfo two-letter names (what CurrentUICulture yields), not hand-typed guesses; plus the legacy aliases.
    public static IEnumerable<object?[]> FollowCases() => new[]
    {
        new object?[] { new CultureInfo("ja-JP").TwoLetterISOLanguageName, "ja" },
        new object?[] { new CultureInfo("ko-KR").TwoLetterISOLanguageName, "ko" },
        new object?[] { new CultureInfo("th-TH").TwoLetterISOLanguageName, "th" },
        new object?[] { new CultureInfo("id-ID").TwoLetterISOLanguageName, "id" },
        new object?[] { new CultureInfo("fil-PH").TwoLetterISOLanguageName, "fil" },
        new object?[] { new CultureInfo("en-US").TwoLetterISOLanguageName, "en" },
        new object?[] { new CultureInfo("de-DE").TwoLetterISOLanguageName, "en" },
        new object?[] { new CultureInfo("zh-CN").TwoLetterISOLanguageName, "en" },
        new object?[] { CultureInfo.InvariantCulture.TwoLetterISOLanguageName, "en" },   // LANG=C
        new object?[] { "in", "id" },    // legacy Indonesian code
        new object?[] { "tl", "fil" },   // Tagalog → Filipino
        new object?[] { "TL", "fil" },
        new object?[] { "", "en" },
        new object?[] { null, "en" },
    };

    [Theory]
    [MemberData(nameof(FollowCases))]
    public void Follow_maps_the_os_ui_culture(string? os, string expected)
    {
        Assert.Equal(expected, LauncherLanguages.FromCulture(os));
        var loc = new LauncherLocalization("follow", () => os!, Catalogs);
        Assert.Equal(expected, loc.ActiveLanguage);
        Assert.Equal(expected, loc.FollowLanguage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("klingon")]
    [InlineData("JA")]
    public void Invalid_setting_falls_back_to_follow(string? setting)
    {
        var loc = Make(setting, os: "ko");
        Assert.Equal("follow", loc.Setting);
        Assert.Equal("ko", loc.ActiveLanguage);
    }

    [Fact]
    public void Fixed_setting_wins_over_the_os_culture()
        => Assert.Equal("ja", Make("ja", os: "ko").ActiveLanguage);

    [Fact]
    public void Resolution_is_active_then_english_then_key()
    {
        var loc = Make("ja");
        Assert.Equal("こんにちは", loc.T("a"));
        Assert.Equal("English only", loc.T("only.en"));
        Assert.Equal("missing.key", loc.T("missing.key"));
    }

    [Fact]
    public void Broken_catalog_falls_back_to_english()
        => Assert.Equal("Hello", Make("th").T("a"));

    [Fact]
    public void Language_without_a_catalog_falls_back_to_english()
        => Assert.Equal("Hello", Make("fil").T("a"));

    [Fact]
    public void TFormat_formats_and_degrades_safely()
    {
        Assert.Equal("1件中2件", Make("ja").TFormat("fmt", 2, 1));
        Assert.Equal("2 of 1", Make("ko").TFormat("fmt", 2, 1));   // ko lacks the key → English template
        Assert.Equal("{0", Make("en").TFormat("bad", 1));           // bad template → template unformatted
        Assert.Equal("nope", Make("en").TFormat("nope", 1));        // missing key → key literal
    }

    [Fact]
    public void LanguageChanged_fires_only_when_the_active_language_changes()
    {
        var loc = Make("follow", os: "ja");
        var fired = 0;
        loc.LanguageChanged += () => fired++;

        loc.SetLanguage("ja");        // follow→ja: setting changed, ACTIVE unchanged
        Assert.Equal(0, fired);
        Assert.Equal("ja", loc.Setting);

        loc.SetLanguage("ko");
        Assert.Equal(1, fired);
        Assert.Equal("안녕하세요", loc.T("a"));

        loc.SetLanguage("ko");        // same → no event
        loc.SetLanguage("bogus");     // invalid → ignored
        Assert.Equal(1, fired);
        Assert.Equal("ko", loc.Setting);

        loc.SetLanguage("follow");    // back to the OS language
        Assert.Equal(2, fired);
        Assert.Equal("ja", loc.ActiveLanguage);
    }

    [Fact]
    public void A_throwing_subscriber_does_not_starve_the_others()
    {
        var loc = Make("en");
        var reached = false;
        loc.LanguageChanged += () => throw new InvalidOperationException("boom");
        loc.LanguageChanged += () => reached = true;
        loc.SetLanguage("ja");
        Assert.True(reached);
    }

    // The completeness pin (framework UiLanguages precedent): every listed language ships an embedded catalog with
    // exactly the English key set, and placeholders match — so a language can never be half-added.
    [Fact]
    public void Every_language_ships_an_embedded_catalog_with_the_english_key_set_and_placeholders()
    {
        var embedded = LauncherLocalization.EmbeddedCatalogs();
        var en = Parse(embedded["en"]);
        Assert.NotEmpty(en);
        foreach (var code in LauncherLanguages.Codes)
        {
            Assert.True(embedded.ContainsKey(code), $"missing embedded Lang/{code}.json");
            var cat = Parse(embedded[code]);
            Assert.Equal(en.Keys.OrderBy(k => k, StringComparer.Ordinal), cat.Keys.OrderBy(k => k, StringComparer.Ordinal));
            foreach (var key in en.Keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(cat[key]), $"{code}:{key} is empty");
                Assert.Equal(Placeholders(en[key]), Placeholders(cat[key]));
            }
        }
    }

    [Fact]
    public void Embedded_catalogs_resolve_through_the_default_constructor()
    {
        var loc = new LauncherLocalization("ko", () => "en");
        Assert.Equal("언어", loc.T("settings.language.title"));
        Assert.Equal("시스템 설정 따르기 (English)", loc.TFormat("settings.language.follow", "English"));
    }

    [Fact]
    public void Language_setting_defaults_to_follow_and_round_trips_through_the_config_store()
    {
        var fs = new MockFileSystem();
        var store = new ConfigStore(fs, new Linux());
        Assert.Equal("follow", store.Load().Launcher.Language);

        var cfg = store.Load();
        cfg.Launcher.Language = "th";
        store.Save(cfg);
        Assert.Equal("th", store.Load().Launcher.Language);
        Assert.Contains("\"language\": \"th\"", fs.File.ReadAllText(store.SettingsPath));
    }

    [Fact]
    public void A_v2_file_written_before_the_language_setting_existed_loads_as_follow()
    {
        var fs = new MockFileSystem();
        var store = new ConfigStore(fs, new Linux());
        fs.AddFile(store.SettingsPath, new MockFileData("""{ "version": 2, "launcher": { "channel": "stable" }, "clients": [] }"""));
        Assert.Equal("follow", store.Load().Launcher.Language);
    }

    private sealed class Linux : IPlatformInfo { public bool IsWindows => false; public string AppDataDir => "/cfg"; }

    private static Dictionary<string, string> Parse(string json) => JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;

    private static string[] Placeholders(string s)
        => System.Text.RegularExpressions.Regex.Matches(s, @"\{\d+[^}]*\}").Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal).ToArray();
}

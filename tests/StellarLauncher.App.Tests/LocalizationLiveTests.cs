using Avalonia.Headless;
using Avalonia;
using StellarLauncher.App.Localization;
using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Localization;
using StellarLauncher.Core.Model;
using Xunit;

// Task 3 review Important #1: the translate-on-display path, plural selection and a VM live re-render. These mutate the
// process-wide Loc/L, so they share the serial Loc collection and reset it around every test.
[Collection(LocStaticCollection.Name)]
public sealed class LocalizationLiveTests : IDisposable
{
    public LocalizationLiveTests() => Loc.ResetForTests();
    public void Dispose() => Loc.ResetForTests();

    private static void Use(string lang) => Loc.Initialize(new LauncherLocalization(lang, () => "en"));

    // ---- LocalizeReviewText (R-2/N-2 protocol rendered per language) ----

    [Fact]
    public void English_is_an_identity_so_the_pinned_strings_hold()
    {
        foreach (var t in new[] { "Added ReShade for Photo Studio", "Added ReShade for Photo Studio · Added X for Y", "Preparing P: Dep…", "launching…" })
            Assert.Equal(t, SessionPresenter.LocalizeReviewText(t));
    }

    [Fact]
    public void Korean_and_japanese_render_each_joined_segment()
    {
        Use("ko");
        Assert.Equal("추가됨: ReShade (Photo Studio용) · 추가됨: X (Y용)",
            SessionPresenter.LocalizeReviewText("Added ReShade for Photo Studio · Added X for Y"));
        Use("ja");
        Assert.Equal(Loc.TFormat("deps.preparing", "Photo Studio", "ReShade, Fonts"),
            SessionPresenter.LocalizeReviewText("Preparing Photo Studio: ReShade, Fonts…"));
        Assert.NotEqual("Preparing Photo Studio: ReShade, Fonts…", Loc.TFormat("deps.preparing", "Photo Studio", "ReShade, Fonts"));
    }

    [Fact]
    public void Unknown_text_passes_through_unchanged()
    {
        Use("ko");
        Assert.Equal("launch failed: runner missing", SessionPresenter.LocalizeReviewText("launch failed: runner missing"));
        Assert.Equal("something else · 추가됨: A (B용)", SessionPresenter.LocalizeReviewText("something else · Added A for B"));
    }

    // Names containing the template's own separators: lines built through ReviewLines are remembered with their parts,
    // so they render exactly, however ambiguous the English text is.
    [Fact]
    public void Remembered_lines_survive_separators_inside_names()
    {
        var added = ReviewLines.Added("Shaders for Photos", "Studio · Pro: Edition");
        var preparing = ReviewLines.Preparing("Tool: Deluxe", "A for B · C");
        Use("ko");
        Assert.Equal(Loc.TFormat("deps.added", "Shaders for Photos", "Studio · Pro: Edition") + " · " + Loc.TFormat("deps.added", "X", "Y"),
            SessionPresenter.LocalizeReviewText(added + " · Added X for Y"));
        Assert.Equal(Loc.TFormat("deps.preparing", "Tool: Deluxe", "A for B · C"), SessionPresenter.LocalizeReviewText(preparing));
        Use("en");
        Assert.Equal(added, SessionPresenter.LocalizeReviewText(added));   // still an English identity
    }

    // ---- plural selection ----

    [Fact]
    public void Plural_picks_one_or_other_and_passes_extra_args()
    {
        Assert.Equal("1 client", Loc.Plural("dash.clients", 1));
        Assert.Equal("2 clients", Loc.Plural("dash.clients", 2));
        Assert.Equal("0 clients", Loc.Plural("dash.clients", 0));
        Assert.Equal("evacuated 1 framework copy to stellar-backups/", L.Plural("logs.evacuated", 1));
        Assert.Equal("evacuated 3 framework copies to stellar-backups/", L.Plural("logs.evacuated", 3));
        Use("ko");
        Assert.Equal(Loc.TFormat("dash.clients.other", 5), Loc.Plural("dash.clients", 5));
    }

    // ---- a VM re-renders on a live switch without losing UI state ----

    private sealed class NoActions : IPluginActions
    {
        public Task InstallAsync(PluginItemViewModel item) => Task.CompletedTask;
        public Task RemoveAsync(PluginItemViewModel item) => Task.CompletedTask;
        public Task EnableAsync(PluginItemViewModel item) => Task.CompletedTask;
        public Task<IReadOnlyList<DependencyStatus>> DependencyStatusAsync(PluginItemViewModel item) => Task.FromResult<IReadOnlyList<DependencyStatus>>(Array.Empty<DependencyStatus>());
        public void SetDependencyUse(PluginItemViewModel item, string dependencyId, bool use) { }
        public bool HasInstallStep(PluginItemViewModel item) => false;
        public Task<bool> DependenciesKeptAsync(PluginItemViewModel item) => Task.FromResult(false);
        public Task RemoveKeptDependenciesAsync(PluginItemViewModel item) => Task.CompletedTask;
        public Task<IReadOnlyList<LedgerEntry>> KeptLedgerEntriesAsync(PluginItemViewModel item) => Task.FromResult<IReadOnlyList<LedgerEntry>>(Array.Empty<LedgerEntry>());
        public Task<IReadOnlyDictionary<string, KeptDependencyDiskState>> KeptDiskStatesAsync(PluginItemViewModel item) => Task.FromResult<IReadOnlyDictionary<string, KeptDependencyDiskState>>(new Dictionary<string, KeptDependencyDiskState>());
        public ISet<string> SkippedOptionalIds(PluginItemViewModel item) => new HashSet<string>();
    }

    // ---- Task 4: per-language plugin presentation on the plugin page ----

    private const string I18nEntryJson = """
    { "id": "photostudio", "name": "Photo Studio", "description": "Take beautiful screenshots.", "author": "StellarProtocol",
      "guideUrl": "https://cdn/ps/guide.md", "guideUrls": { "ja": "https://cdn/ps/guide.ja.md" },
      "media": [ { "type": "youtube", "url": "https://www.youtube.com/watch?v=abc", "caption": "Tour" },
                 { "type": "youtube", "url": "https://www.youtube.com/watch?v=def", "caption": "Poses" } ],
      "i18n": { "ja": { "name": "フォトスタジオ", "description": "美しい写真を。", "captions": [null, "ポーズ"] } },
      "versions": [ { "version": "1.7.0", "dllUrl": "https://cdn/ps.dll", "sha256": "x", "minModSystemVersion": "2.0.0",
                      "changelog": { "added": ["Korean"], "changed": [], "fixed": ["Jitter"], "removed": [] },
                      "changelogI18n": { "ja": { "added": ["韓国語"] } } } ] }
    """;

    private sealed class Guides : HttpMessageHandler
    {
        public readonly List<string> Requested = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var url = r.RequestUri!.ToString();
            lock (Requested) Requested.Add(url);
            var body = url.EndsWith("guide.ja.md") ? "# ガイド" : url.EndsWith("guide.md") ? "# Guide" : "";
            return Task.FromResult(new HttpResponseMessage(body.Length > 0 ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.NotFound)
                { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task Plugin_page_shows_the_launcher_language_and_refetches_the_guide_on_a_switch()
    {
        var loc = new LauncherLocalization("en", () => "en");
        Loc.Initialize(loc);
        var entry = System.Text.Json.JsonSerializer.Deserialize<PluginEntry>(I18nEntryJson, PluginRegistry.JsonOptions)!;
        var item = new PluginItemViewModel(entry, installed: false, installedVersion: null, installedFramework: "2.8.0", new NoActions());
        var guides = new Guides();
        await item.EnsureDetailLoadedAsync(new HttpClient(guides), _ => { });
        Assert.Equal("Photo Studio", item.Name);
        Assert.Equal("# Guide", item.GuideMarkdown);
        Assert.Equal(new[] { "Tour", "Poses" }, item.Media.Select(m => m.Caption));

        var names = new List<string?>();
        item.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        loc.SetLanguage("ja");
        for (var i = 0; i < 50 && item.GuideMarkdown != "# ガイド"; i++) await Task.Delay(10);

        Assert.Equal("フォトスタジオ", item.Name);
        Assert.Equal("美しい写真を。", item.Description);
        Assert.Equal(new[] { "Tour", "ポーズ" }, item.Media.Select(m => m.Caption));   // caption 0 untranslated → English
        Assert.Equal("# ガイド", item.GuideMarkdown);                                      // re-fetched in the new language
        Assert.Equal("https://cdn/ps/guide.ja.md", item.GuideBaseUrl);                     // images resolve beside the ja guide
        var card = Assert.Single(item.ChangelogVersions);
        Assert.Equal(new[] { "韓国語" }, card.Changelog!.Added);
        Assert.Equal(new[] { "Jitter" }, card.Changelog.Fixed);
        Assert.Contains(nameof(PluginItemViewModel.ChangelogVersions), names);

        loc.SetLanguage("ko");   // no ko guide → back to the English one
        for (var i = 0; i < 50 && item.GuideMarkdown != "# Guide"; i++) await Task.Delay(10);
        Assert.Equal("# Guide", item.GuideMarkdown);
        Assert.Equal("Photo Studio", item.Name);
    }

    private sealed class HeldGuides : HttpMessageHandler
    {
        public readonly TaskCompletionSource HoldJa = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var url = r.RequestUri!.ToString();
            if (url.EndsWith("guide.ja.md")) { await HoldJa.Task; return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("# ガイド") }; }
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("# Guide") };
        }
    }

    // Two guide requests in flight (ja held, then a switch to ko → English): the NEWEST wins, whatever finishes last.
    [Fact]
    public async Task Overlapping_guide_requests_apply_only_the_newest()
    {
        var loc = new LauncherLocalization("en", () => "en");
        Loc.Initialize(loc);
        var entry = System.Text.Json.JsonSerializer.Deserialize<PluginEntry>(I18nEntryJson, PluginRegistry.JsonOptions)!;
        var item = new PluginItemViewModel(entry, false, null, "2.8.0", new NoActions());
        var web = new HeldGuides();
        await item.EnsureDetailLoadedAsync(new HttpClient(web), _ => { });
        Assert.Equal("# Guide", item.GuideMarkdown);

        loc.SetLanguage("ja");                      // ja request starts and is held
        loc.SetLanguage("ko");                      // no ko guide → English request, completes
        for (var i = 0; i < 50 && item.HasGuideStatus; i++) await Task.Delay(10);
        Assert.Equal("# Guide", item.GuideMarkdown);
        web.HoldJa.SetResult();                     // the stale ja response arrives last
        await Task.Delay(100);
        Assert.Equal("# Guide", item.GuideMarkdown);
        Assert.Equal("https://cdn/ps/guide.md", item.GuideBaseUrl);
        Assert.False(item.HasGuideStatus);
    }

    // The dependency notice keeps its English protocol text but shows the plugin's localized name.
    [Fact]
    public void Review_lines_show_the_plugin_display_name_and_stay_english_internally()
    {
        var entry = System.Text.Json.JsonSerializer.Deserialize<PluginEntry>(I18nEntryJson, PluginRegistry.JsonOptions)!;
        var line = ReviewLines.Added("ReShade", entry.Name, entry);
        Assert.Equal("Added ReShade for Photo Studio", line);
        Assert.Equal(line, SessionPresenter.LocalizeReviewText(line));      // en identity
        Use("ja");
        Assert.Equal(Loc.TFormat("deps.added", "ReShade", "フォトスタジオ"), SessionPresenter.LocalizeReviewText(line));
        var prep = ReviewLines.Preparing(entry.Name, "ReShade", entry);
        Assert.Equal(Loc.TFormat("deps.preparing", "フォトスタジオ", "ReShade"), SessionPresenter.LocalizeReviewText(prep));
    }

    [Fact]
    public void Review_line_memory_is_bounded_and_evicts_oldest_first()
    {
        Use("ko");
        var first = ReviewLines.Added("Dep0", "Evict Plugin · 0");
        for (var i = 1; i <= 600; i++) ReviewLines.Added($"Dep{i}", $"Evict Plugin · {i}");
        var newest = ReviewLines.Added("DepNew", "Evict Plugin · new");
        // The newest is still remembered (exact parts); the oldest fell back to template parsing, which splits at " · ".
        Assert.Equal(Loc.TFormat("deps.added", "DepNew", "Evict Plugin · new"), SessionPresenter.LocalizeReviewText(newest));
        Assert.NotEqual(Loc.TFormat("deps.added", "Dep0", "Evict Plugin · 0"), SessionPresenter.LocalizeReviewText(first));
    }

    // Task 4 fix round #1: Avalonia 12.0.4 reuses the previous run's fallback face ignoring weight — every run whose
    // formatting changed opens with a zero-width space so CJK/Thai bold renders bold.
    [Fact]
    public async Task Markdown_runs_open_with_a_boundary_when_formatting_changes()
    {
        using var session = Avalonia.Headless.HeadlessUnitTestSession.StartNew(typeof(HeadlessEntry));
        var texts = await session.Dispatch(() =>
        {
            var inl = StellarLauncher.Core.Services.MarkdownParser.ParseInlines("**撮影**タブの**非表示**グループ and **more** text");
            var tb = StellarLauncher.App.Views.MarkdownView.RenderText(inl, 13, Avalonia.Media.Brushes.White, null);
            return tb.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => (r.Text, Bold: r.FontWeight == Avalonia.Media.FontWeight.Bold)).ToList();
        }, CancellationToken.None);
        const string Z = StellarLauncher.App.Views.MarkdownView.FormattingBoundary;
        Assert.Equal(new[]
        {
            ("撮影", true), (Z + "タブの", false), (Z + "非表示", true), (Z + "グループ and ", false), ("more", true), (" text", false),   // Latin: untouched
        }, texts.ToArray());
    }

    // The per-run font probe as a test: real Skia shaping + system fonts. Every bold CJK/Thai/Hangul run must be shaped
    // with a BOLD face. Runs only where a CJK face with a Bold weight exists (e.g. Noto Sans CJK); elsewhere (a CI image
    // without CJK fonts) it returns early — the run-structure test above still pins the workaround.
    [Fact]
    public async Task Bold_cjk_thai_and_hangul_runs_are_shaped_with_a_bold_face()
    {
        using var session = Avalonia.Headless.HeadlessUnitTestSession.StartNew(typeof(SkiaHeadlessEntry));
        var failures = await session.Dispatch(() =>
        {
            var fm = Avalonia.Media.FontManager.Current;
            if (!fm.TryMatchCharacter('撮', Avalonia.Media.FontStyle.Normal, Avalonia.Media.FontWeight.Bold, Avalonia.Media.FontStretch.Normal,
                    null, null, out var cjk) || cjk.GlyphTypeface.Weight < Avalonia.Media.FontWeight.SemiBold)
                return new List<string>();   // no bold CJK face on this machine: nothing to measure
            var md = "**撮影**タブの**非表示**グループ、**他の冒険者**、**NPC**、**敵**、**収集物** · **포토** 스튜디오는 **설정** 탭의 **숨기기**에서 · **ปลั๊กอิน**นี้**ช่วย**ถ่าย";
            var tb = StellarLauncher.App.Views.MarkdownView.RenderText(StellarLauncher.Core.Services.MarkdownParser.ParseInlines(md), 14, Avalonia.Media.Brushes.White, null);
            var w = new Avalonia.Controls.Window { Content = tb, Width = 4000, Height = 200 };
            w.Show();
            tb.Measure(new Avalonia.Size(4000, 200));
            // Intended weight per character comes from the INLINES (the run properties Avalonia hands the shaper are
            // exactly what the bug corrupts, so they can't be the oracle).
            var intended = new List<bool>();
            foreach (var r in tb.Inlines!.OfType<Avalonia.Controls.Documents.Run>())
                intended.AddRange(Enumerable.Repeat(r.FontWeight >= Avalonia.Media.FontWeight.Bold, r.Text?.Length ?? 0));
            var bad = new List<string>();
            foreach (var line in tb.TextLayout.TextLines)
            {
                var offset = line.FirstTextSourceIndex;
                foreach (var run in line.TextRuns)
                {
                    if (run is Avalonia.Media.TextFormatting.ShapedTextRun shaped)
                    {
                        var text = shaped.Text.ToString();
                        var isBold = shaped.GlyphRun.GlyphTypeface.Weight >= Avalonia.Media.FontWeight.SemiBold;
                        for (var i = 0; i < text.Length; i++)
                            if (StellarLauncher.App.Views.MarkdownView.NeedsFallbackFont(text[i]) && offset + i < intended.Count && intended[offset + i] != isBold)
                                bad.Add($"'{text[i]}' wanted {(intended[offset + i] ? "bold" : "regular")}, shaped {shaped.GlyphRun.GlyphTypeface.FamilyName} {shaped.GlyphRun.GlyphTypeface.Weight}");
                    }
                    offset += run.Length;
                }
            }
            w.Close();
            return bad;
        }, CancellationToken.None);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public async Task A_missing_translated_guide_falls_back_to_the_english_one()
    {
        Use("ja");
        var json = I18nEntryJson.Replace("guide.ja.md", "guide.missing.md");
        var entry = System.Text.Json.JsonSerializer.Deserialize<PluginEntry>(json, PluginRegistry.JsonOptions)!;
        var item = new PluginItemViewModel(entry, false, null, "2.8.0", new NoActions());
        await item.EnsureDetailLoadedAsync(new HttpClient(new Guides()), _ => { });
        Assert.Equal("# Guide", item.GuideMarkdown);
        Assert.Equal("https://cdn/ps/guide.md", item.GuideBaseUrl);
        Assert.False(item.HasGuideStatus);
    }

    [Fact]
    public void Plugin_page_labels_switch_language_and_an_open_confirm_stays_open()
    {
        var loc = new LauncherLocalization("en", () => "en");
        Loc.Initialize(loc);
        var entry = WorkspaceFixture.Entry("photostudio", "Photo Studio", "2.0.0", "1.7.0");
        var item = new PluginItemViewModel(entry, installed: true, installedVersion: "1.6.0", installedFramework: "2.8.0", new NoActions());
        Assert.Equal("Update to v1.7.0", item.InstallLabel);
        item.RequestInstallCommand.Execute(null);
        Assert.True(item.ConfirmVisible);

        loc.SetLanguage("ko");

        Assert.Equal(Loc.TFormat("ov.action.updateTo", "1.7.0"), item.InstallLabel);
        Assert.NotEqual("Update to v1.7.0", item.InstallLabel);
        Assert.Equal(Loc.TFormat("detail.installedV", "1.6.0"), item.InstalledBadge);
        Assert.True(item.ConfirmVisible);   // the switch re-renders text only — it never cancels what the player opened
        Assert.True(item.IsUpdate);
    }
}

/// <summary>Headless entry with REAL Skia text shaping and system font fallback (for font-selection tests).</summary>
public sealed class SkiaHeadlessEntry
{
    public static Avalonia.AppBuilder BuildAvaloniaApp() => Avalonia.AppBuilder.Configure<HeadlessTestApp>()
        .UseSkia()
        .UseHeadless(new Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont();
}

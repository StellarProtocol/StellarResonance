using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StellarLauncher.App.Localization;
using StellarLauncher.App.Views;
using StellarLauncher.Core.Localization;
using Xunit;

// Loc is a process-wide static: every test that mutates it lives in this collection, which never runs in parallel with
// anything, and each test leaves Loc back on the fixed-English default.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocStaticCollection { public const string Name = "Loc static (serial)"; }

[Collection(LocStaticCollection.Name)]
public sealed class LocalizationTests : IDisposable
{
    public LocalizationTests() => Loc.ResetForTests();
    public void Dispose() => Loc.ResetForTests();

    private sealed class Recorder : IObserver<string>
    {
        public readonly List<string> Values = new();
        public void OnNext(string value) => Values.Add(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    private static LauncherLocalization Fresh(string setting) => new(setting, () => "en");

    [Fact]
    public void Localized_text_emits_now_and_again_on_every_language_change()
    {
        var loc = Fresh("en");
        Loc.Initialize(loc);
        var rec = new Recorder();
        using var sub = new LocalizedText("settings.language.title").Subscribe(rec);
        Assert.Equal(new[] { "LANGUAGE" }, rec.Values);

        loc.SetLanguage("ja");
        loc.SetLanguage("ko");
        Assert.Equal(new[] { "LANGUAGE", "言語", "언어" }, rec.Values);

        sub.Dispose();
        loc.SetLanguage("en");
        Assert.Equal(3, rec.Values.Count);   // disposed → no more updates
    }

    [Fact]
    public void Markup_extension_keeps_its_key()
        => Assert.Equal("settings.language.hint", new TExtension("settings.language.hint").Key);

    // A page's labels are rebuilt on every navigation: the app-lifetime service must not pin them.
    [Fact]
    public void Subscribers_are_held_weakly_once_their_token_is_dropped()
    {
        var loc = Fresh("en");
        Loc.Initialize(loc);
        var weak = SubscribeAndDrop();
        Collect();
        Assert.False(weak.IsAlive);
        loc.SetLanguage("ja");   // pruning a dead entry must not throw
        Assert.Equal(0, Loc.SubscriberCount);
    }

    // …but a live token (what a binding expression holds) keeps even an otherwise-unreferenced observer alive.
    [Fact]
    public void A_held_token_keeps_its_observer_alive()
    {
        var loc = Fresh("en");
        Loc.Initialize(loc);
        var (token, weak) = SubscribeAndKeepToken();
        Collect();
        Assert.True(weak.IsAlive);
        loc.SetLanguage("th");
        Assert.Equal("ภาษา", ((Recorder)weak.Target!).Values[^1]);
        GC.KeepAlive(token);
    }

    // Labels are created far more often than the language changes: dead entries must be swept on Add too, not only on a
    // Raise, or the list grows for the whole session.
    [Fact]
    public void Dead_entries_are_swept_when_the_list_doubles_without_any_language_change()
    {
        var loc = Fresh("en");
        Loc.Initialize(loc);
        SubscribeAndDropMany(64);            // sweeps at 16/32/64 find them alive; next sweep at 128
        Collect();
        var kept = new List<IDisposable>();
        for (var i = 0; i < 64; i++) kept.Add(new LocalizedText("settings.language.title").Subscribe(new Recorder()));
        Assert.Equal(64, Loc.SubscriberCount);   // the 128th Add swept the 64 dead entries
        GC.KeepAlive(kept);
    }

    [Fact]
    public void Reinitialising_detaches_the_previous_service()
    {
        var first = Fresh("en");
        Loc.Initialize(first);
        var rec = new Recorder();
        using var sub = new LocalizedText("settings.language.title").Subscribe(rec);
        var second = Fresh("en");
        Loc.Initialize(second);
        first.SetLanguage("ja");             // the old service no longer drives the UI
        Assert.Single(rec.Values);
        second.SetLanguage("ko");
        Assert.Equal("언어", rec.Values[^1]);
    }

    [Fact]
    public void A_throwing_subscriber_does_not_stop_the_others()
    {
        var loc = Fresh("en");
        Loc.Initialize(loc);
        var owner = new object();
        using var bad = Loc.Subscribe(owner, _ => throw new InvalidOperationException("boom"));
        var rec = new Recorder();
        using var good = new LocalizedText("settings.language.title").Subscribe(rec);
        loc.SetLanguage("ko");
        Assert.Equal("언어", rec.Values[^1]);
    }

    // The real thing: compiled XAML {loc:T} labels in LauncherSettingsView switch live, and a SetLanguage from a
    // background thread is marshalled to the UI thread (the composition root's marshal) before any label is touched.
    [Fact]
    public async Task Real_xaml_labels_switch_live_and_off_thread_changes_land_on_the_ui_thread()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessEntry));
        await session.Dispatch(async () =>
        {
            var loc = Fresh("en");
            int? raisedOn = null;
            Loc.Initialize(loc, Loc.AvaloniaUiThread);
            using var probe = Loc.Subscribe(new object(), _ => raisedOn = Environment.CurrentManagedThreadId);
            var uiThread = Environment.CurrentManagedThreadId;

            var window = new Window { Content = new LauncherSettingsView(), Width = 1100, Height = 760 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            string[] Labels() => window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
            Assert.Contains("LANGUAGE", Labels());
            Assert.Contains("Launcher language", Labels());

            loc.SetLanguage("ko");                                   // on the UI thread → inline
            Assert.Contains("언어", Labels());
            Assert.Contains("런처 언어", Labels());
            Assert.DoesNotContain("LANGUAGE", Labels());

            await Task.Run(() => loc.SetLanguage("ja"));             // off the UI thread → posted
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("言語", Labels());
            Assert.Contains("ランチャーの表示言語", Labels());
            Assert.Equal(uiThread, raisedOn);
            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    private static void Collect()
    {
        for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SubscribeAndDrop()
    {
        var rec = new Recorder();
        _ = new LocalizedText("settings.language.title").Subscribe(rec);
        return new WeakReference(rec);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SubscribeAndDropMany(int n)
    {
        for (var i = 0; i < n; i++) _ = new LocalizedText("settings.language.title").Subscribe(new Recorder());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (IDisposable Token, WeakReference Weak) SubscribeAndKeepToken()
    {
        var rec = new Recorder();
        var token = new LocalizedText("settings.language.title").Subscribe(rec);
        return (token, new WeakReference(rec));
    }
}

/// <summary>Headless Avalonia entry point for XAML tests: Fluent + the launcher's own theme/styles, no real drawing.</summary>
public sealed class HeadlessEntry
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<HeadlessTestApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

public sealed class HeadlessTestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        var baseUri = new Uri("avares://StellarLauncher.App/");
        Resources.MergedDictionaries.Add(new ResourceInclude(baseUri) { Source = new Uri("avares://StellarLauncher.App/Styles/Theme.axaml") });
        Styles.Add(new StyleInclude(baseUri) { Source = new Uri("avares://StellarLauncher.App/Styles/Styles.axaml") });
    }
}

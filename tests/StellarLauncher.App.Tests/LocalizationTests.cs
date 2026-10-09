using System.Runtime.CompilerServices;
using StellarLauncher.App.Localization;
using StellarLauncher.Core.Localization;
using Xunit;

// Loc is the app-wide static access point — every test that touches it lives in this one class (no parallel sharing).
public class LocalizationTests
{
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
        for (var i = 0; i < 3 && weak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Assert.False(weak.IsAlive);
        loc.SetLanguage("ja");   // pruning a dead entry must not throw
    }

    // …but a live token (what a binding expression holds) keeps even an otherwise-unreferenced observer alive.
    [Fact]
    public void A_held_token_keeps_its_observer_alive()
    {
        var loc = Fresh("en");
        Loc.Initialize(loc);
        var (token, weak) = SubscribeAndKeepToken();
        for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Assert.True(weak.IsAlive);
        loc.SetLanguage("th");
        Assert.Equal("ภาษา", ((Recorder)weak.Target!).Values[^1]);
        GC.KeepAlive(token);
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SubscribeAndDrop()
    {
        var rec = new Recorder();
        _ = new LocalizedText("settings.language.title").Subscribe(rec);
        return new WeakReference(rec);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (IDisposable Token, WeakReference Weak) SubscribeAndKeepToken()
    {
        var rec = new Recorder();
        var token = new LocalizedText("settings.language.title").Subscribe(rec);
        return (token, new WeakReference(rec));
    }
}

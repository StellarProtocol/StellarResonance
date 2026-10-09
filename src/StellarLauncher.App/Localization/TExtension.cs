using System;
using Avalonia;
using Avalonia.Markup.Xaml;

namespace StellarLauncher.App.Localization;

/// <summary>
/// <c>{loc:T some.key}</c> — a localized string that re-resolves live when the launcher language changes (no restart).
/// Declare <c>xmlns:loc="using:StellarLauncher.App.Localization"</c> on the view. Works on any Avalonia property
/// (<c>Text</c>, <c>Content</c>, <c>ToolTip.Tip</c>, <c>PlaceholderText</c>, …).
/// </summary>
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string key) => Key = key;

    /// <summary>The catalog key.</summary>
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => new LocalizedText(Key).ToBinding();
}

/// <summary>
/// An observable localized string: emits <c>Loc.T(key)</c> on subscribe and again on every language change. The
/// observer (the binding on the control) is held WEAKLY through <see cref="Loc.Subscribe{T}"/>, so the app-lifetime
/// service never pins a page that has been navigated away from.
/// </summary>
public sealed class LocalizedText(string key) : IObservable<string>
{
    public string Key { get; } = key;

    public IDisposable Subscribe(IObserver<string> observer)
    {
        observer.OnNext(Loc.T(Key));
        var key = Key;
        return Loc.Subscribe(observer, o => o.OnNext(Loc.T(key)));
    }
}

using System;
using System.Collections.Generic;
using StellarLauncher.Core.Localization;

namespace StellarLauncher.App.Localization;

/// <summary>
/// The app-wide access point to the launcher's <see cref="ILauncherLocalization"/> — set ONCE by the composition root
/// (<c>App.OnFrameworkInitializationCompleted</c>) before any view loads, so the <c>{loc:T key}</c> markup extension and
/// view-models resolve through the same service. Until then (designer, unit tests) it is a fixed-English instance.
/// </summary>
public static class Loc
{
    private static ILauncherLocalization _service = new LauncherLocalization(LauncherLanguages.English);
    private static readonly WeakLanguageHub Hub = new();

    /// <summary>The active localization service.</summary>
    public static ILauncherLocalization Service => _service;

    /// <summary>Install the app's service (composition root only).</summary>
    public static void Initialize(ILauncherLocalization service)
    {
        _service.LanguageChanged -= Hub.Raise;
        _service = service;
        _service.LanguageChanged += Hub.Raise;
    }

    /// <summary>Resolve a key through the active service.</summary>
    public static string T(string key) => _service.T(key);

    /// <summary>Resolve and format a key through the active service.</summary>
    public static string TFormat(string key, params object?[] args) => _service.TFormat(key, args);

    /// <summary>Call <paramref name="onChanged"/>(owner) on every language change for as long as <paramref name="owner"/>
    /// is alive. The owner is held WEAKLY, so a label or page view-model rebuilt on every navigation is never kept alive by
    /// the app-lifetime service — which is why the callback receives the owner instead of capturing it (a callback that
    /// captures its owner would pin it). The returned token keeps the owner alive while the token itself is held.</summary>
    public static IDisposable Subscribe<T>(T owner, Action<T> onChanged) where T : class
        => Hub.Add(owner, o => onChanged((T)o));

    // Weak subscriber list: the service lives for the whole app, bound labels and page VMs do not.
    private sealed class WeakLanguageHub
    {
        private readonly List<Entry> _subs = new();

        public IDisposable Add(object owner, Action<object> onChanged)
        {
            var entry = new Entry(new WeakReference<object>(owner), onChanged);
            lock (_subs) _subs.Add(entry);
            return new Unsubscribe(owner, () => { lock (_subs) _subs.Remove(entry); });
        }

        public void Raise()
        {
            List<(object Owner, Action<object> OnChanged)> live = new();
            lock (_subs)
            {
                _subs.RemoveAll(e => !e.Owner.TryGetTarget(out _));
                foreach (var e in _subs)
                    if (e.Owner.TryGetTarget(out var o)) live.Add((o, e.OnChanged));
            }
            foreach (var (o, a) in live)
            {
                try { a(o); }
                catch (Exception) { /* one broken label must not stop the rest from switching */ }
            }
        }
    }

    private sealed record Entry(WeakReference<object> Owner, Action<object> OnChanged);

    // The token holds its owner STRONGLY: whoever keeps the subscription (a binding expression keeps the IDisposable its
    // observable returned) also keeps the owner, even when the owner is a wrapper observer nothing else references.
    private sealed class Unsubscribe(object owner, Action dispose) : IDisposable
    {
        private object? _owner = owner;
        private Action? _dispose = dispose;
        public void Dispose() { _dispose?.Invoke(); _dispose = null; _owner = null; }
        public override string ToString() => $"loc subscription ({_owner?.GetType().Name ?? "disposed"})";
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using StellarLauncher.Core.Localization;

namespace StellarLauncher.App.Localization;

/// <summary>
/// The app-wide access point to the launcher's <see cref="ILauncherLocalization"/> — set ONCE by the composition root
/// (<c>App.OnFrameworkInitializationCompleted</c>) before any view loads, so the <c>{loc:T key}</c> markup extension and
/// view-models resolve through the same service. Until then (designer, unit tests) it is a fixed-English instance.
/// </summary>
public static class Loc
{
    private static readonly WeakLanguageHub Hub = new();

    /// <summary>The active localization service (shared with Core's <see cref="L"/>).</summary>
    public static ILauncherLocalization Service => L.Current;

    /// <summary>Install the app's service (composition root only). <paramref name="toUiThread"/> marshals the live-change
    /// fan-out onto the UI thread (bound labels must only be touched there); omitted = run inline on the raising thread.</summary>
    public static void Initialize(ILauncherLocalization service, Action<Action>? toUiThread = null)
    {
        L.Current.LanguageChanged -= OnServiceLanguageChanged;
        L.Current = service;
        _toUiThread = toUiThread;
        service.LanguageChanged += OnServiceLanguageChanged;
    }

    /// <summary>Tests only: back to the fixed-English default with no subscribers and inline raising.</summary>
    internal static void ResetForTests()
    {
        Initialize(L.English());
        Hub.Clear();
    }

    /// <summary>Tests only: entries currently held by the hub — live ones plus any dead ones not yet swept.</summary>
    internal static int SubscriberCount => Hub.Count;

    /// <summary>Resolve a key through the active service.</summary>
    public static string T(string key) => L.T(key);

    /// <summary>Resolve and format a key through the active service.</summary>
    public static string TFormat(string key, params object?[] args) => L.TFormat(key, args);

    /// <summary>Count-dependent text (<c>keyBase.one</c> / <c>keyBase.other</c>, count = {0}); see <see cref="L.Plural"/>.</summary>
    public static string Plural(string keyBase, long count, params object?[] args) => L.Plural(keyBase, count, args);

    /// <summary>Call <paramref name="onChanged"/>(owner) on every language change for as long as <paramref name="owner"/>
    /// is alive. The owner is held WEAKLY, so a label or page view-model rebuilt on every navigation is never kept alive by
    /// the app-lifetime service — which is why the callback receives the owner instead of capturing it (a callback that
    /// captures its owner would pin it). The returned token keeps the owner alive while the token itself is held.</summary>
    public static IDisposable Subscribe<T>(T owner, Action<T> onChanged) where T : class
        => Hub.Add(owner, o => onChanged((T)o));

    private static Action<Action>? _toUiThread;

    /// <summary>The UI-thread marshal for <see cref="Initialize"/> in an Avalonia app: run inline when already on the UI
    /// thread, else post to the dispatcher. The ONE definition — the composition root and the headless tests both use it.</summary>
    public static readonly Action<Action> AvaloniaUiThread =
        a => { if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) a(); else Avalonia.Threading.Dispatcher.UIThread.Post(a); };

    private static void OnServiceLanguageChanged()
    {
        if (_toUiThread is { } post) post(Hub.Raise);
        else Hub.Raise();
    }

    // Weak subscriber list: the service lives for the whole app, bound labels and page VMs do not.
    private sealed class WeakLanguageHub
    {
        private const int MinSweepThreshold = 16;
        private readonly List<Entry> _subs = new();
        private int _sweepAt = MinSweepThreshold;

        public int Count { get { lock (_subs) return _subs.Count; } }

        public IDisposable Add(object owner, Action<object> onChanged)
        {
            var entry = new Entry(new WeakReference<object>(owner), onChanged);
            lock (_subs)
            {
                _subs.Add(entry);
                // Labels are created far more often than the language changes: without this, entries of dead pages would
                // only be pruned on a Raise and the list would grow for the whole session. Amortised O(1): sweep when the
                // list has doubled since the last sweep.
                if (_subs.Count >= _sweepAt)
                {
                    SweepLocked();
                    _sweepAt = Math.Max(MinSweepThreshold, _subs.Count * 2);
                }
            }
            return new Unsubscribe(owner, () => { lock (_subs) _subs.Remove(entry); });
        }

        public void Clear() { lock (_subs) { _subs.Clear(); _sweepAt = MinSweepThreshold; } }

        public void Raise()
        {
            List<(object Owner, Action<object> OnChanged)> live = new();
            lock (_subs)
            {
                SweepLocked();
                foreach (var e in _subs)
                    if (e.Owner.TryGetTarget(out var o)) live.Add((o, e.OnChanged));
            }
            foreach (var (o, a) in live)
            {
                try { a(o); }
                catch (Exception ex) { Trace.WriteLine($"[Loc] LanguageChanged subscriber threw (others still switch): {ex}"); }
            }
        }

        private void SweepLocked() => _subs.RemoveAll(e => !e.Owner.TryGetTarget(out _));
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

using System;

namespace StellarLauncher.Core.Localization;

/// <summary>
/// Process-wide access to the launcher's localization for code that composes user-visible text deep in Core (launch
/// status lines, dependency notices…) where threading a service through every constructor would be noise. The App's
/// composition root installs the real service (via <c>Loc.Initialize</c>) before any UI exists; until then — and in
/// Core unit tests — it is a fixed-English instance, so every string reads exactly as the English catalog says.
/// UI that must re-render on a language change subscribes through the App's <c>Loc.Subscribe</c>, never here.
/// </summary>
public static class L
{
    private static ILauncherLocalization _current = English();

    /// <summary>The active localization service.</summary>
    public static ILauncherLocalization Current
    {
        get => _current;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Resolve a key (active → English → key).</summary>
    public static string T(string key) => _current.T(key);

    /// <summary>Resolve and <c>string.Format</c> a key.</summary>
    public static string TFormat(string key, params object?[] args) => _current.TFormat(key, args);

    /// <summary>Count-dependent text: <c>&lt;keyBase&gt;.one</c> when <paramref name="count"/> is 1, else
    /// <c>&lt;keyBase&gt;.other</c>; the count is <c>{0}</c>, further <paramref name="args"/> are <c>{1}</c>…</summary>
    public static string Plural(string keyBase, long count, params object?[] args)
    {
        var all = new object?[args.Length + 1];
        all[0] = count;
        Array.Copy(args, 0, all, 1, args.Length);
        return _current.TFormat(keyBase + (count == 1 ? ".one" : ".other"), all);
    }

    /// <summary>A fresh fixed-English service (the default, and what tests reset to).</summary>
    public static ILauncherLocalization English() => new LauncherLocalization(LauncherLanguages.English, () => LauncherLanguages.English);
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StellarLauncher.App.Localization;
using StellarLauncher.Core.Model;

namespace StellarLauncher.App.Services;

/// <summary>
/// The dependency review's progress lines — "Preparing &lt;plugin&gt;: &lt;deps&gt;…" and "Added &lt;dep&gt; for
/// &lt;plugin&gt;", N-2-joined with " · " — stay ENGLISH end to end: they are the protocol <see cref="ClientSessions"/>
/// classifies the sticky notice on, the always-on log line, and what the R-2/N-2 pins assert. They are rendered in the
/// active language only at display time (<see cref="Localize"/>).
/// <para>To survive names that themselves contain " for ", ": " or " · ", every line the runner emits is built here and
/// REMEMBERED with its parts; <see cref="Localize"/> first consumes remembered lines verbatim (longest match at each
/// position), and only falls back to parsing the template for text it never saw (e.g. a line from a test double).</para>
/// </summary>
public static class ReviewLines
{
    private const string Join = " · ";
    private const int MaxRemembered = 512;   // a few lines per launch; bounded so a long session can't grow it forever
    private sealed record Line(string Key, string A, string B, PluginEntry? Entry, bool PluginIsA);
    private static readonly ConcurrentDictionary<string, Line> Known = new(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<string> Order = new();   // insertion order, for oldest-first eviction
    private static readonly Regex AddedRx = new(@"^Added (.+?) for (.+)$", RegexOptions.CultureInvariant);
    private static readonly Regex PreparingRx = new(@"^Preparing (.+?): (.+)…$", RegexOptions.CultureInvariant);

    /// <summary>"Added &lt;dependency&gt; for &lt;plugin&gt;" (English protocol line), remembered for display.</summary>
    public static string Added(string dependency, string plugin, PluginEntry? entry = null) =>
        Remember($"Added {dependency} for {plugin}", new Line("deps.added", dependency, plugin, entry, PluginIsA: false));

    /// <summary>"Preparing &lt;plugin&gt;: &lt;names&gt;…" (English protocol line), remembered for display.</summary>
    public static string Preparing(string plugin, string names, PluginEntry? entry = null) =>
        Remember($"Preparing {plugin}: {names}…", new Line("deps.preparing", plugin, names, entry, PluginIsA: true));

    /// <summary>The (possibly " · "-joined) protocol text in the active language; unknown text passes through.</summary>
    public static string Localize(string text)
    {
        var parts = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            if (LongestKnownAt(text, i) is { } k && Known.TryGetValue(k, out var line))   // TryGetValue: may be evicted meanwhile
            {
                parts.Add(Render(line));
                i += k.Length;
            }
            else
            {
                var end = text.IndexOf(Join, i, StringComparison.Ordinal);
                var seg = end < 0 ? text[i..] : text[i..end];
                parts.Add(ParseFallback(seg));
                i += seg.Length;
            }
            if (i < text.Length && string.CompareOrdinal(text, i, Join, 0, Join.Length) == 0) i += Join.Length;
        }
        return string.Join(Join, parts);
    }

    private static string? LongestKnownAt(string text, int i) => Known.Keys
        .Where(k => string.CompareOrdinal(text, i, k, 0, k.Length) == 0 && i + k.Length <= text.Length
                    && (i + k.Length == text.Length || string.CompareOrdinal(text, i + k.Length, Join, 0, Join.Length) == 0))
        .OrderByDescending(k => k.Length).FirstOrDefault();

    // The plugin is shown by its display name in the active language (registry i18n); the protocol string keeps English.
    private static string Render(Line l)
    {
        var lang = Loc.Service.ActiveLanguage;
        var (a, b) = l.Entry is { } e ? (l.PluginIsA ? (e.DisplayName(lang), l.B) : (l.A, e.DisplayName(lang))) : (l.A, l.B);
        return Loc.TFormat(l.Key, a, b);
    }

    private static string ParseFallback(string seg)
    {
        if (AddedRx.Match(seg) is { Success: true } a) return Loc.TFormat("deps.added", a.Groups[1].Value, a.Groups[2].Value);
        if (PreparingRx.Match(seg) is { Success: true } p) return Loc.TFormat("deps.preparing", p.Groups[1].Value, p.Groups[2].Value);
        return seg;
    }

    private static string Remember(string english, Line line)
    {
        if (Known.TryAdd(english, line)) Order.Enqueue(english);
        else Known[english] = line;
        while (Known.Count > MaxRemembered && Order.TryDequeue(out var oldest)) Known.TryRemove(oldest, out _);   // oldest first
        return english;
    }
}

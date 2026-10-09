using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StellarLauncher.App.Localization;

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
    private static readonly ConcurrentDictionary<string, (string Key, string A, string B)> Known = new(StringComparer.Ordinal);
    private static readonly Regex AddedRx = new(@"^Added (.+?) for (.+)$", RegexOptions.CultureInvariant);
    private static readonly Regex PreparingRx = new(@"^Preparing (.+?): (.+)…$", RegexOptions.CultureInvariant);

    /// <summary>"Added &lt;dependency&gt; for &lt;plugin&gt;" (English protocol line), remembered for display.</summary>
    public static string Added(string dependency, string plugin) => Remember("deps.added", $"Added {dependency} for {plugin}", dependency, plugin);

    /// <summary>"Preparing &lt;plugin&gt;: &lt;names&gt;…" (English protocol line), remembered for display.</summary>
    public static string Preparing(string plugin, string names) => Remember("deps.preparing", $"Preparing {plugin}: {names}…", plugin, names);

    /// <summary>The (possibly " · "-joined) protocol text in the active language; unknown text passes through.</summary>
    public static string Localize(string text)
    {
        var parts = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            if (LongestKnownAt(text, i) is { } k)
            {
                var (key, a, b) = Known[k];
                parts.Add(Loc.TFormat(key, a, b));
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

    private static string ParseFallback(string seg)
    {
        if (AddedRx.Match(seg) is { Success: true } a) return Loc.TFormat("deps.added", a.Groups[1].Value, a.Groups[2].Value);
        if (PreparingRx.Match(seg) is { Success: true } p) return Loc.TFormat("deps.preparing", p.Groups[1].Value, p.Groups[2].Value);
        return seg;
    }

    private static string Remember(string key, string english, string a, string b)
    {
        if (Known.Count >= MaxRemembered) Known.Clear();
        Known[english] = (key, a, b);
        return english;
    }
}

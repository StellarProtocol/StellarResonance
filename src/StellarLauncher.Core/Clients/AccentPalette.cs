using System;
using System.Collections.Generic;
using System.Linq;

namespace StellarLauncher.Core.Clients;

/// <summary>Client accent colours (identity), spec § 6 — assigned in order, skipping colours in use.</summary>
public static class AccentPalette
{
    public static readonly IReadOnlyList<string> Defaults = new[]
    {
        "#37c8e0", "#ffb347", "#ff6ec7", "#7c5cff", "#2dd4bf", "#a3e635", "#ff6b6b",
    };

    public static string Next(IEnumerable<string> inUse)
    {
        var list = inUse as ICollection<string> ?? inUse.ToList();
        var used = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        var free = Defaults.FirstOrDefault(c => !used.Contains(c));
        return free ?? Defaults[list.Count % Defaults.Count];
    }
}

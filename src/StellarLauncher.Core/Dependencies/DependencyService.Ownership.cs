using System.Linq;

namespace StellarLauncher.Core.Dependencies;

/// <summary>I4: ownership of a destination is per (plugin, dependency) — a path recorded by a different
/// plugin's ledger, or by a different dependency id in the SAME ledger, is foreign even though it's
/// tracked somewhere. M11: path comparisons here are ordinal (case-sensitive).</summary>
public sealed partial class DependencyService
{
    private bool IsForeign(string gameMini, string pluginId, string dependencyId, string abs)
    {
        if (!_fs.File.Exists(abs)) return false;
        var rel = DependencyPaths.Relative(gameMini, abs);
        var owner = FindOwner(gameMini, rel);
        return owner is null || owner.Value.PluginId != pluginId || owner.Value.DependencyId != dependencyId;
    }

    private (string PluginId, string DependencyId)? FindOwner(string gameMini, string relativePath)
    {
        foreach (var ledger in _store.ReadAll(gameMini))
        foreach (var entry in ledger.Entries)
        foreach (var f in entry.Files)
        {
            if (DependencyPaths.FromLedger(gameMini, f.Path) is null) continue; // I2: a corrupt entry claims nothing
            if (f.Path == relativePath) // M11: ordinal
                return (ledger.PluginId, entry.DependencyId);
        }
        return null;
    }
}

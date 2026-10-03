using System.Linq;

namespace StellarLauncher.Core.Dependencies;

/// <summary>I4/M1: ownership of a destination is per (plugin, dependency), checked whether or not the
/// destination currently exists, is parked, or is absent — a path recorded by a different plugin's
/// ledger, or by a different dependency id in the SAME ledger, is foreign either way. M11: path
/// comparisons here are ordinal (case-sensitive). I2: a destination we already own, that currently
/// exists but no longer matches our own recorded hash, is the player's edit — it is never overwritten
/// and is dropped from the ledger so it is never touched again.</summary>
public sealed partial class DependencyService
{
    /// <summary>Null means "safe to place"; otherwise the Blocked status EnsureOneAsync should return
    /// for the WHOLE dependency without writing anything.</summary>
    private DependencyStatus? CheckDestination(string gameMini, string pluginId, string dependencyId, LedgerEntry? existing, string abs)
    {
        var rel = DependencyPaths.Relative(gameMini, abs);
        var owner = FindOwner(gameMini, rel);
        if (owner is not null && (owner.Value.PluginId != pluginId || owner.Value.DependencyId != dependencyId))
            return new DependencyStatus(dependencyId, DependencyState.Blocked, rel); // M1: foreign regardless of presence

        if (owner is null)
            return _fs.File.Exists(abs) ? new DependencyStatus(dependencyId, DependencyState.Blocked, rel) : null;

        if (!_fs.File.Exists(abs)) return null; // ours, but currently absent (e.g. parked) — safe to (re)write

        var recorded = existing?.Files.FirstOrDefault(f => f.Path == rel);
        if (recorded is not null && !DependencyFileHash.Matches(_fs, abs, recorded.Sha256))
        {
            // Minor 1 (round 4): a prior attempt's incomplete rollback can leave the live file holding
            // NEWER bytes than the ledger while its .stellar-bak still holds what the ledger expects —
            // that's our own half-finished repair, not the player's edit, so it's safe to overwrite.
            if (DependencyFileHash.Matches(_fs, abs + ".stellar-bak", recorded.Sha256)) return null;

            DropFileFromLedger(gameMini, pluginId, dependencyId, rel); // I2
            return new DependencyStatus(dependencyId, DependencyState.Blocked, $"{rel} (modified)");
        }
        return null;
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

    /// <summary>I2: removes just one file from one dependency's ledger entry (dropping the whole entry if
    /// it was the last file), so a player-modified destination is never considered ours again.</summary>
    private void DropFileFromLedger(string gameMini, string pluginId, string dependencyId, string relPath)
    {
        var ledger = _store.Read(gameMini, pluginId);
        var entry = ledger.Entries.FirstOrDefault(e => e.DependencyId == dependencyId);
        if (entry is null) return;
        var remaining = entry.Files.Where(f => f.Path != relPath).ToList();
        var entries = ledger.Entries.Where(e => e.DependencyId != dependencyId).ToList();
        if (remaining.Count > 0) entries.Add(entry with { Files = remaining });
        _store.Write(gameMini, new DependencyLedger(pluginId, entries));
    }
}

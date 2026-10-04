using System;
using System.Collections.Generic;
using System.Linq;
using StellarLauncher.Core.Model;

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
        var foreign = ForeignClaim(gameMini, pluginId, dependencyId, abs, quarantine: true, out var ours);
        if (foreign is not null || !ours) return foreign; // not ours: Blocked if claimed/occupied, else safe to place

        if (!_fs.File.Exists(abs)) return null; // ours, but currently absent (e.g. parked) — safe to (re)write

        var recorded = existing?.Files.FirstOrDefault(f => f.Path == rel);
        if (recorded is not null && !DependencyFileHash.Matches(_fs, abs, recorded.Sha256))
        {
            // Minor 1 (round 4): a prior attempt's incomplete rollback can leave the live file holding
            // NEWER bytes than the ledger while its .stellar-bak still holds what the ledger expects —
            // that's our own half-finished repair, not the player's edit, so it's safe to overwrite.
            var bak = abs + ".stellar-bak";
            if (DependencyFileHash.Matches(_fs, bak, recorded.Sha256))
            {
                // Minor 2 (round 6): restore the known-good backup over the live file FIRST, establishing
                // it as the correct baseline before the normal backup-and-replace cycle runs. Otherwise
                // WriteAll's own backup step would take a fresh (wrong, mid-repair) backup and destroy the
                // last good copy — so a SECOND failure during this very retry would orphan the file for good.
                _fs.File.Move(bak, abs, overwrite: true);
                return null;
            }

            // Final review I2: a live file holding exactly the bytes our own pending record says we were placing
            // is our interrupted update, not the player's edit.
            if (IsOurPendingFile(gameMini, pluginId, dependencyId, rel, abs, quarantine: true)) return null;

            // Final review M-e: once dropped from the ledger, a parked copy of this file would be orphaned
            // forever (nothing would ever restore or remove it) — delete it now, but only if it is still our
            // exact recorded bytes.
            TryDeleteParkedCopy(gameMini, pluginId, rel, recorded.Sha256);
            DropFileFromLedger(gameMini, pluginId, dependencyId, rel); // I2
            return new DependencyStatus(dependencyId, DependencyState.Blocked, $"{rel} (modified)", DependencyReason.PlayerFile);
        }
        return null;
    }

    private void TryDeleteParkedCopy(string gameMini, string pluginId, string rel, string sha256)
    {
        var parked = DependencyPaths.ParkedPath(gameMini, pluginId, rel);
        try { if (DependencyFileHash.Matches(_fs, parked, sha256)) _fs.File.Delete(parked); }
        catch { /* best effort — the Blocked outcome stands either way */ }
    }

    /// <summary>The read-only half of <see cref="CheckDestination"/>, shared with <see cref="Status"/>:
    /// Blocked (detail = the game_mini-relative path) when another (plugin, dependency) claims
    /// <paramref name="abs"/> — M1: whether or not it exists — or when nobody claims it and a file is there
    /// (the player's own file). <paramref name="ours"/> says whether this (plugin, dependency) owns it.
    /// Reads only; <paramref name="quarantine"/> false never renames a corrupt ledger.</summary>
    private DependencyStatus? ForeignClaim(string gameMini, string pluginId, string dependencyId, string abs,
        bool quarantine, out bool ours)
    {
        var rel = DependencyPaths.Relative(gameMini, abs);
        var owner = FindOwner(gameMini, rel, quarantine);
        ours = owner is not null && owner.Value.PluginId == pluginId && owner.Value.DependencyId == dependencyId;
        if (owner is not null && !ours) return new DependencyStatus(dependencyId, DependencyState.Blocked, rel, DependencyReason.OtherOwner);
        if (owner is null && _fs.File.Exists(abs))
        {
            // Final review I2: an unowned file is the player's — UNLESS this very (plugin, dependency)'s pending
            // record lists it with these exact bytes: then the launcher placed it and died before committing.
            // A byte-identical file with no such record (the player's own copy) is never adopted.
            if (IsOurPendingFile(gameMini, pluginId, dependencyId, rel, abs, quarantine)) { ours = true; return null; }
            return new DependencyStatus(dependencyId, DependencyState.Blocked, rel, DependencyReason.PlayerFile);
        }
        return null;
    }

    /// <summary>Final review I2: true only when this plugin's ledger has a pending record for
    /// <paramref name="dependencyId"/> listing <paramref name="rel"/>, and the file there hashes to the sha256
    /// that record expected.</summary>
    private bool IsOurPendingFile(string gameMini, string pluginId, string dependencyId, string rel, string abs, bool quarantine)
    {
        var pending = _store.Read(gameMini, pluginId, quarantine).Pending;
        var f = pending?.Where(e => e.DependencyId == dependencyId).SelectMany(e => e.Files).FirstOrDefault(x => x.Path == rel);
        return f is not null && DependencyFileHash.Matches(_fs, abs, f.Sha256);
    }

    /// <summary>Task 6: the destinations knowable WITHOUT downloading — a file-kind dependency's single
    /// <c>to</c>, and a zip's exact-entry mappings (<c>from</c> naming one entry). A zip directory mapping
    /// (<c>from</c> ending in '/') places whatever the archive holds, so it can't be checked up front.
    /// Paths the manifest isn't allowed to use are skipped (EnsureAsync reports those).</summary>
    private static IEnumerable<string> KnownDestinations(string gameMini, string pluginId, PluginDependency d)
    {
        var tos = d.Kind == "zip"
            ? d.Files.Where(f => f.From is not null && !f.From.EndsWith('/')).Select(f => f.To)
            : d.Files.Take(1).Select(f => f.To);
        foreach (var to in tos)
            if (DependencyPaths.Resolve(gameMini, pluginId, d.Target, to) is { } abs) yield return abs;
    }

    /// <summary>Read-only Blocked check for a dependency that isn't installed: the first known destination
    /// that is claimed by someone else or occupied by an unowned file.</summary>
    private DependencyStatus? StatusBlocked(string gameMini, string pluginId, PluginDependency d)
    {
        foreach (var abs in KnownDestinations(gameMini, pluginId, d))
            if (ForeignClaim(gameMini, pluginId, d.Id, abs, quarantine: false, out _) is { } blocked) return blocked;
        return null;
    }

    /// <summary>R-1: <see cref="StatusBlocked"/>'s write-path twin — called from <see cref="EnsureOneAsync"/>
    /// before a download, so a destination already known foreign/unowned is reported Blocked without ever
    /// fetching bytes for it. <c>quarantine: true</c> (unlike the read-only <see cref="StatusBlocked"/>),
    /// matching every other ownership check <see cref="EnsureOneAsync"/>'s own <see cref="CheckDestination"/>
    /// makes.</summary>
    private DependencyStatus? StatusBlockedForWrite(string gameMini, string pluginId, PluginDependency d)
    {
        foreach (var abs in KnownDestinations(gameMini, pluginId, d))
            if (ForeignClaim(gameMini, pluginId, d.Id, abs, quarantine: true, out _) is { } blocked) return blocked;
        return null;
    }

    private (string PluginId, string DependencyId)? FindOwner(string gameMini, string relativePath, bool quarantine = true)
    {
        foreach (var ledger in _store.ReadAll(gameMini, quarantine))
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
        // Important (round 6): see DependencyLedgerStore.TryReadForWrite — never overwrite a ledger we
        // simply failed to read this instant.
        if (!_store.TryReadForWrite(gameMini, pluginId, out var ledger))
            throw new InvalidOperationException("dependency record could not be read");
        var entry = ledger.Entries.FirstOrDefault(e => e.DependencyId == dependencyId);
        if (entry is null) return;
        var remaining = entry.Files.Where(f => f.Path != relPath).ToList();
        var entries = ledger.Entries.Where(e => e.DependencyId != dependencyId).ToList();
        if (remaining.Count > 0) entries.Add(entry with { Files = remaining });
        _store.Write(gameMini, ledger with { Entries = entries }); // keeps any pending record (I2)
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Dependencies;

/// <summary>v3 (spec § 12): the KEPT and REINSTALL-REQUESTED ledger flags, and what an ensure does with them.</summary>
public sealed partial class DependencyService
{
    public Task SetKeptAsync(string gameMini, string pluginId, bool kept, CancellationToken ct = default) =>
        LockedAsync(gameMini, () => WriteFlags(gameMini, pluginId, l => l with { Kept = kept }), ct);

    public Task RequestReinstallAsync(string gameMini, string pluginId, CancellationToken ct = default) =>
        LockedAsync(gameMini, () => WriteFlags(gameMini, pluginId, l => l with { ReinstallRequested = true }), ct);

    public bool IsKept(string gameMini, string pluginId) =>
        DependencyPaths.IsValidPluginId(pluginId) && _store.Read(gameMini, pluginId, quarantine: false).Kept;

    public IReadOnlyList<LedgerEntry> LedgerEntries(string gameMini, string pluginId) =>
        DependencyPaths.IsValidPluginId(pluginId) ? _store.Read(gameMini, pluginId, quarantine: false).Entries : Array.Empty<LedgerEntry>();

    /// <summary>I-2: all-files-must-match at ONE location, same "every recorded file" rule <see cref="IsInstalled"/>
    /// uses for the live location — a dependency with several files (a zip) is "Kept" only when every one of them
    /// is present there, "Parked" only when every one of them is present (matching) at its parked copy instead,
    /// else "Missing". An entry with no files at all is Missing (nothing to find).</summary>
    public KeptDependencyDiskState KeptDiskState(string gameMini, string pluginId, string dependencyId)
    {
        if (!DependencyPaths.IsValidPluginId(pluginId)) return KeptDependencyDiskState.Missing;
        var entry = _store.Read(gameMini, pluginId, quarantine: false).Entries.FirstOrDefault(e => e.DependencyId == dependencyId);
        if (entry is null || entry.Files.Count == 0) return KeptDependencyDiskState.Missing;
        if (entry.Files.All(f => DependencyPaths.FromLedger(gameMini, f.Path) is { } abs && DependencyFileHash.Matches(_fs, abs, f.Sha256)))
            return KeptDependencyDiskState.Kept;
        if (entry.Files.All(f => DependencyFileHash.Matches(_fs, DependencyPaths.ParkedPath(gameMini, pluginId, f.Path), f.Sha256)))
            return KeptDependencyDiskState.Parked;
        return KeptDependencyDiskState.Missing;
    }

    /// <summary>Read-for-write (round 6: a present-but-unreadable ledger is never written blind), change, write back
    /// only when something changed. No ledger → nothing to mark (an empty ledger is never written into existence).</summary>
    private void WriteFlags(string gameMini, string pluginId, Func<DependencyLedger, DependencyLedger> change)
    {
        if (!DependencyPaths.IsValidPluginId(pluginId)) return;
        if (!_store.TryReadForWrite(gameMini, pluginId, out var ledger))
            throw new InvalidOperationException("dependency record could not be read");
        if (ledger.Entries.Count == 0 && ledger.Pending is not { Count: > 0 }) return;
        var changed = change(ledger);
        if (!Equals(changed, ledger)) _store.Write(gameMini, changed);
    }

    /// <summary>V3: an ensure runs only for an installed plugin, so a KEPT ledger is adopted again (flag cleared;
    /// <see cref="RemoveUndeclared"/> has already dropped what the new version no longer declares). V2: when a
    /// reinstall was requested, returns the ids of every declared, non-skipped dependency to FORCE through the
    /// main loop below — never deletes anything itself. Final-review I-1: a premature delete-then-download here
    /// left a dependency with NO file in place the instant a download failed (the window between Adopt's delete
    /// and the main loop's own download/verify/commit) — the old file must stay Installed until a replacement is
    /// actually ready. The main loop's <see cref="EnsureOneAsync"/> does the forcing (skips its "already
    /// installed" short-circuit) and goes through its own transactional update path (backup, replace, rollback
    /// on failure) — <see cref="DependencyService.Ownership"/>'s <c>CheckDestination</c> still blocks a file the
    /// player changed, exactly as for a normal update. Works from <paramref name="seen"/> (the ledger
    /// RemoveUndeclared already read) and reads NOTHING more: the fault-injection tests pin the exact number of
    /// ledger reads an ensure makes. The Kept clear is independent best effort (a failure there just leaves it
    /// for the next ensure to adopt). Whether the REINSTALL flag should now be cleared is decided by the caller
    /// once every dependency's real outcome is known (<see cref="EnsureCoreAsync"/>) — true only when every
    /// forced dependency comes back Installed; a RETRY (the flag still set) forces every non-skipped dependency
    /// again next time, not just the one(s) that failed before — there is no per-dependency "already redone"
    /// memory.</summary>
    private HashSet<string>? Adopt(string gameMini, string pluginId, DependencyLedger seen,
        IReadOnlyList<PluginDependency> deps, ISet<string> skippedIds)
    {
        if (seen.Kept)
        {
            try { WriteFlags(gameMini, pluginId, l => l with { Kept = false }); }
            catch (Exception) { /* best effort: the next ensure adopts it */ }
        }
        if (!seen.ReinstallRequested) return null;
        return deps.Where(d => !skippedIds.Contains(d.Id) && DependencyDeclaration.Problem(d) is null)
            .Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>V2: the request is consumed once every declared, non-skipped dependency's remove ran clean (an ensure
    /// that removed every entry also removed the ledger, and with it the flag). Best effort — a failure to WRITE the
    /// clear itself means one harmless extra re-download next time (the dependency was already removed/replaced).</summary>
    private void ClearReinstallRequest(string gameMini, string pluginId)
    {
        try { WriteFlags(gameMini, pluginId, l => l with { ReinstallRequested = false }); }
        catch (Exception) { /* best effort */ }
    }

    /// <summary>Review carry-over (a): GATED — never a bare fail-open <see cref="IsKept"/> read raced against a
    /// separate <see cref="DependencyService.RemoveAllAsync"/>. Runs under the same per-folder lock as every other
    /// mutating member.</summary>
    public Task<bool> RemoveAllUnlessKeptAsync(string gameMini, string pluginId, CancellationToken ct = default) =>
        LockedAsync(gameMini, () => RemoveAllUnlessKeptCore(gameMini, pluginId), ct);

    /// <summary>Round 6 rule applies here too: a present-but-unreadable ledger is never treated as "safe to sweep" —
    /// it throws, exactly like <see cref="RemoveAllCore"/>, so the caller's existing failure handling (never a silent
    /// removal) covers it. Only a successfully read, NOT-kept ledger is actually removed.</summary>
    private bool RemoveAllUnlessKeptCore(string gameMini, string pluginId)
    {
        if (!DependencyPaths.IsValidPluginId(pluginId)) return false;
        if (!_store.TryReadForWrite(gameMini, pluginId, out var ledger))
            throw new InvalidOperationException("dependency record could not be read");
        if (ledger.Kept) return false;
        RemoveAllFiles(gameMini, pluginId, ledger);
        return true;
    }
}

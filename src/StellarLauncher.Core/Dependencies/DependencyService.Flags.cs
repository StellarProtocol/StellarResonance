using System;
using System.Collections.Generic;
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
    /// <see cref="RemoveUndeclared"/> has already dropped what the new version no longer declares). V2: when a reinstall
    /// was requested, each declared, non-skipped dependency's files are removed first — hash-checked, so a file the
    /// player changed stays and the loop then reports it Blocked — and the loop downloads + verifies them afresh.
    /// Works from <paramref name="seen"/> (the ledger RemoveUndeclared already read) and reads NOTHING more unless a flag
    /// is set: the fault-injection tests pin the exact number of ledger reads an ensure makes. The Kept clear is
    /// independent best effort (a failure there just leaves it for the next ensure to adopt). Review carry-over (b):
    /// returns whether the REINSTALL flag should now be cleared — false when it was never set, or when any
    /// dependency's remove failed, so the flag stays and the next ensure retries just the ones still wrong (clearing
    /// it unconditionally would silently drop a dependency that was never actually re-downloaded).</summary>
    private bool Adopt(string gameMini, string pluginId, DependencyLedger seen,
        IReadOnlyList<PluginDependency> deps, ISet<string> skippedIds)
    {
        if (seen.Kept)
        {
            try { WriteFlags(gameMini, pluginId, l => l with { Kept = false }); }
            catch (Exception) { /* best effort: the next ensure adopts it */ }
        }
        if (!seen.ReinstallRequested) return false;
        var allRemoved = true;
        foreach (var d in deps)
        {
            if (skippedIds.Contains(d.Id) || DependencyDeclaration.Problem(d) is not null) continue;
            try { RemoveCore(gameMini, pluginId, d.Id); }
            catch (Exception) { allRemoved = false; /* the ensure below reports whatever is still wrong; flag stays for a retry */ }
        }
        return allRemoved;
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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Dependencies;

/// <summary>Generic dependency installer (see <see cref="IDependencyService"/>). Placement/download
/// mechanics live in the <c>.Placement</c> partial, zip handling in <c>.Zip</c>, ownership checks in
/// <c>.Ownership</c>; vanilla-launch parking in <see cref="DependencyParking"/>.</summary>
public sealed partial class DependencyService : IDependencyService
{
    private readonly IFileSystem _fs;
    private readonly HttpClient _http;
    private readonly DependencyLedgerStore _store;
    private readonly DependencyParking _parking;
    // Task 6 fix round, Important 1: one gate per game folder, held by every MUTATING member for its whole
    // run (EnsureAsync spans a download between its ledger read and its ledger write). Read-only Status
    // never takes it. Not re-entrant — internal callers use the *Core methods. Only ever awaited (round 2).
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>Final review I4: a download that delivers no bytes for this long is abandoned ("download timed
    /// out") — a stalled server must never hold a launch forever.</summary>
    public TimeSpan DownloadInactivityTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Final review I4: no single download may take longer than this, however steadily it trickles.</summary>
    public TimeSpan DownloadOverallTimeout { get; init; } = TimeSpan.FromMinutes(10);

    public DependencyService(IFileSystem fs, HttpClient http)
    {
        _fs = fs;
        _http = http;
        _store = new DependencyLedgerStore(fs);
        _parking = new DependencyParking(fs, _store);
    }

    public async Task<IReadOnlyList<DependencyStatus>> EnsureAsync(string gameMini, string pluginId,
        IReadOnlyList<PluginDependency> deps, ISet<string> skippedIds, CancellationToken ct)
    {
        var gate = Gate(gameMini);
        // Fix round 2: ConfigureAwait(false) on every await in this service — none of it needs the caller's
        // (UI) context, and a continuation queued behind a busy UI thread would hold the gate meanwhile.
        await gate.WaitAsync(ct).ConfigureAwait(false);
        // Off the caller's thread even when the gate was free (WaitAsync then completes synchronously): the
        // ledger reads and hashing below must never run on a UI thread.
        try { return await Task.Run(() => EnsureCoreAsync(gameMini, pluginId, deps, skippedIds, ct)).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    private async Task<IReadOnlyList<DependencyStatus>> EnsureCoreAsync(string gameMini, string pluginId,
        IReadOnlyList<PluginDependency> deps, ISet<string> skippedIds, CancellationToken ct)
    {
        // Controller round: pluginId becomes a path segment (ledger file, stellar/deps/<id>/…) in every
        // branch below — reject it up front with the same rule DependencyLedgerStore uses for file stems,
        // rather than letting a bogus id reach the filesystem.
        if (!DependencyPaths.IsValidPluginId(pluginId))
            return deps.Select(d => new DependencyStatus(d.Id, DependencyState.Failed, "invalid plugin id")).ToList();

        // Final review I3: FIRST, so a dependency renamed by a plugin update can reuse its old destination.
        var seen = RemoveUndeclared(gameMini, pluginId, deps);
        // v3 V3/V2: adopt a kept ledger; prepare a requested reinstall (no extra ledger read unless a flag is set).
        var reinstall = Adopt(gameMini, pluginId, seen, deps, skippedIds);

        var results = new Dictionary<string, DependencyStatus>();
        foreach (var d in deps)
        {
            ct.ThrowIfCancellationRequested();
            // I5/M-a/M-b: a declaration the launcher refuses is never downloaded, and nothing is touched.
            if (DependencyDeclaration.Problem(d) is { } problem)
                results[Key(d)] = new DependencyStatus(d.Id, DependencyState.Failed, problem);
            else if (GateStatus(d, skippedIds, results) is { } gate)
                results[Key(d)] = ApplyGate(gameMini, pluginId, d, gate);
            else
                results[Key(d)] = await EnsureGuardedAsync(gameMini, pluginId, d, ct).ConfigureAwait(false);
        }
        if (reinstall) ClearReinstallRequest(gameMini, pluginId);
        return deps.Select(d => results[Key(d)]).ToList();
    }

    /// <summary>A gated dependency: a Skip removes what it placed (I6: an IO failure there is Failed, never a
    /// throw — and, being ledger handling, eligible for the unreadable note, minor 1); any other gate status
    /// ("requires … listed earlier", "waiting for …") is returned bare, never annotated.</summary>
    private DependencyStatus ApplyGate(string gameMini, string pluginId, PluginDependency d, DependencyStatus gate)
    {
        if (gate.State != DependencyState.Skipped) return gate;
        try { RemoveCore(gameMini, pluginId, d.Id); return gate; }
        catch (Exception ex) { return AnnotateIfCorrupt(gameMini, pluginId, new DependencyStatus(d.Id, DependencyState.Failed, ex.Message)); }
    }

    /// <summary><see cref="EnsureOneAsync"/>, turning anything but the caller's own cancel into a status.</summary>
    private async Task<DependencyStatus> EnsureGuardedAsync(string gameMini, string pluginId, PluginDependency d, CancellationToken ct)
    {
        try
        {
            var status = await EnsureOneAsync(gameMini, pluginId, d, ct).ConfigureAwait(false);
            // A Failed returned HERE (not thrown) is always a download/verify failure — never annotated (minor 1).
            return status.State == DependencyState.Blocked ? AnnotateIfCorrupt(gameMini, pluginId, status) : status;
        }
        // Fix round 1, Important 1: EnsureOneAsync already converts an uncancelled OCE (e.g. an HttpClient
        // timeout) to a Failed status rather than throwing — this guard is defense in depth for the same rule.
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Anything that escaped as an exception came from placement or ledger handling — eligible.
            return AnnotateIfCorrupt(gameMini, pluginId, new DependencyStatus(d.Id, DependencyState.Failed, ex.Message));
        }
    }

    /// <summary>A dependency's results key — "" for a declaration with no id at all (reported Failed).</summary>
    private static string Key(PluginDependency d) => d.Id ?? "";

    /// <summary>Final review I3: a dependency this plugin no longer declares (dropped or renamed by an update —
    /// or every one of them, when the new version declares none) is removed exactly like an unticked one:
    /// hash-checked, so a file the player changed since is left alone. Best-effort — an unreadable ledger or
    /// an IO failure leaves it for the next run and never fails the dependencies that ARE declared.
    /// Returns the ledger it read (v3: <see cref="Adopt"/> takes the flags from it, so an ensure makes no extra
    /// ledger read).</summary>
    private DependencyLedger RemoveUndeclared(string gameMini, string pluginId, IReadOnlyList<PluginDependency> deps)
    {
        var declared = deps.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        var ledger = _store.Read(gameMini, pluginId, quarantine: false);
        var stale = ledger.Entries.Concat(ledger.Pending ?? Array.Empty<LedgerEntry>())
            .Select(e => e.DependencyId).Where(id => !declared.Contains(id)).Distinct().ToList();
        foreach (var id in stale)
        {
            try { RemoveCore(gameMini, pluginId, id); }
            catch (Exception) { /* best effort: retried on the next ensure */ }
        }
        return ledger;
    }

    public IReadOnlyList<DependencyStatus> Status(string gameMini, string pluginId,
        IReadOnlyList<PluginDependency> deps, ISet<string> skippedIds)
    {
        // Fix round 1, Minor 5: same shape as EnsureAsync for an invalid id — one Failed "invalid plugin
        // id" entry per dependency, not an empty list (which was indistinguishable from "no dependencies
        // declared").
        if (!DependencyPaths.IsValidPluginId(pluginId))
            return deps.Select(d => new DependencyStatus(d.Id, DependencyState.Failed, "invalid plugin id")).ToList();

        // Minor 3: Status is read-only — it must not have the side effect of quarantining a corrupt
        // ledger (it still reads one as empty, via Read's own fallback, either way).
        var ledger = _store.Read(gameMini, pluginId, quarantine: false);
        var results = new Dictionary<string, DependencyStatus>();
        foreach (var d in deps)
        {
            if (DependencyDeclaration.Problem(d) is { } problem)
            {
                results[Key(d)] = new DependencyStatus(d.Id, DependencyState.Failed, problem);
                continue;
            }
            var gate = GateStatus(d, skippedIds, results);
            if (gate is not null) { results[d.Id] = gate; continue; }
            var entry = ledger.Entries.FirstOrDefault(e => e.DependencyId == d.Id);
            // Task 6: a destination already occupied by a file nobody's ledger owns (or claimed by another
            // dependency) reads as Blocked here too, exactly as EnsureAsync would report it — read-only.
            results[d.Id] = IsInstalled(gameMini, entry, d)
                ? new DependencyStatus(d.Id, DependencyState.Installed, null)
                : StatusBlocked(gameMini, pluginId, d) ?? new DependencyStatus(d.Id, DependencyState.NotInstalled, null);
        }
        return deps.Select(d => results[Key(d)]).ToList();
    }

    public Task RemoveAsync(string gameMini, string pluginId, string dependencyId, CancellationToken ct = default) =>
        LockedAsync(gameMini, () => RemoveCore(gameMini, pluginId, dependencyId), ct);

    private void RemoveCore(string gameMini, string pluginId, string dependencyId)
    {
        // Controller round: an invalid id names no ledger of ours — a no-op, not a throw (there is
        // nothing to read or fail to read).
        if (!DependencyPaths.IsValidPluginId(pluginId)) return;

        // Important (round 6): a present-but-unreadable ledger must never be treated as empty here —
        // that would silently write/delete over data this call simply couldn't read right now.
        if (!_store.TryReadForWrite(gameMini, pluginId, out var ledger))
            throw new InvalidOperationException("dependency record could not be read");
        var entry = ledger.Entries.FirstOrDefault(e => e.DependencyId == dependencyId);
        var pending = ledger.Pending?.FirstOrDefault(e => e.DependencyId == dependencyId);
        if (entry is null && pending is null) return;
        if (entry is not null) DeleteEntryFiles(gameMini, pluginId, entry);
        if (pending is not null) DeleteEntryFiles(gameMini, pluginId, pending); // I2: hash-checked, our own bytes only
        _store.Write(gameMini, ledger with
        {
            Entries = ledger.Entries.Where(e => e.DependencyId != dependencyId).ToList(),
            Pending = ledger.PendingWithout(dependencyId),
        });
    }

    public Task RemoveAllAsync(string gameMini, string pluginId, CancellationToken ct = default) =>
        LockedAsync(gameMini, () => RemoveAllCore(gameMini, pluginId), ct);

    private void RemoveAllCore(string gameMini, string pluginId)
    {
        if (!DependencyPaths.IsValidPluginId(pluginId)) return; // controller round: see Remove above
        if (!_store.TryReadForWrite(gameMini, pluginId, out var ledger))
            throw new InvalidOperationException("dependency record could not be read");
        RemoveAllFiles(gameMini, pluginId, ledger);
    }

    /// <summary>Shared by <see cref="RemoveAllCore"/> and <see cref="RemoveAllUnlessKeptCore"/> once each has its
    /// own already-read, already-validated ledger in hand — never re-reads.</summary>
    private void RemoveAllFiles(string gameMini, string pluginId, DependencyLedger ledger)
    {
        foreach (var entry in ledger.Entries.Concat(ledger.Pending ?? Array.Empty<LedgerEntry>()))
            DeleteEntryFiles(gameMini, pluginId, entry);
        _store.Write(gameMini, ledger with { Entries = Array.Empty<LedgerEntry>(), Pending = null });
    }

    public Task ParkModdedOnlyAsync(string gameMini, CancellationToken ct = default) =>
        LockedAsync(gameMini, () => _parking.Park(gameMini), ct);

    public Task UnparkModdedOnlyAsync(string gameMini, IReadOnlySet<string>? keepParked = null, CancellationToken ct = default) =>
        LockedAsync(gameMini, () => _parking.Unpark(gameMini, keepParked), ct);

    /// <summary>The folder's gate. The key is the full path without a trailing separator (case-insensitive
    /// on Windows), so "/g", "/g/" and "/x/../g" share one gate.</summary>
    private SemaphoreSlim Gate(string gameMini)
    {
        var key = _fs.Path.GetFullPath(gameMini).TrimEnd('/', '\\');
        return _gates.GetOrAdd(key.Length == 0 ? gameMini : key, _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>Fix round 2, Critical: the gate is only ever AWAITED — never a blocking Wait(). A caller on
    /// the UI thread blocked in Wait() behind an EnsureAsync whose continuations need that same thread would
    /// deadlock the launcher. The work itself runs on the pool once the gate is held.</summary>
    private async Task LockedAsync(string gameMini, Action action, CancellationToken ct)
    {
        var gate = Gate(gameMini);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try { await Task.Run(action).ConfigureAwait(false); }   // never on the caller's (UI) thread, even with a free gate
        finally { gate.Release(); }
    }

    /// <summary>Same contract as <see cref="LockedAsync(string,Action,CancellationToken)"/>, for the one mutating
    /// member (<see cref="RemoveAllUnlessKeptAsync"/>) that needs to report back whether it actually removed anything.</summary>
    private async Task<T> LockedAsync<T>(string gameMini, Func<T> func, CancellationToken ct)
    {
        var gate = Gate(gameMini);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await Task.Run(func).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    public IReadOnlyList<string> LedgerPluginIds(string gameMini) => _store.PluginIds(gameMini);

    /// <summary>R1/R2: resolves whether <paramref name="d"/> is gated by <paramref name="skippedIds"/> or
    /// by its <c>requires</c> chain, given every earlier dependency's outcome in <paramref name="resultsSoFar"/>.
    /// Null means "not gated — evaluate normally". A forward reference (R2) and a Failed prerequisite both
    /// report Failed without removing anything; only a Skipped/Blocked prerequisite propagates a Skip.</summary>
    private static DependencyStatus? GateStatus(PluginDependency d, ISet<string> skippedIds,
        IReadOnlyDictionary<string, DependencyStatus> resultsSoFar)
    {
        if (skippedIds.Contains(d.Id))
            return new DependencyStatus(d.Id, DependencyState.Skipped, null);

        foreach (var r in d.Requires ?? Array.Empty<string>())
        {
            if (!resultsSoFar.TryGetValue(r, out var rs))
                return new DependencyStatus(d.Id, DependencyState.Failed, $"requires {r} listed earlier");
            // M-d: a Blocked prerequisite (or one itself waiting) makes this one WAIT; a prerequisite the
            // player skipped makes it plainly Skipped.
            if (rs.State == DependencyState.Blocked || rs.Reason == DependencyReason.WaitingForPrerequisite)
                return new DependencyStatus(d.Id, DependencyState.Skipped, r, DependencyReason.WaitingForPrerequisite);
            if (rs.State == DependencyState.Skipped)
                return new DependencyStatus(d.Id, DependencyState.Skipped, null);
            if (rs.State == DependencyState.Failed)
                return new DependencyStatus(d.Id, DependencyState.Failed, $"waiting for {r}");
        }
        return null;
    }

    /// <summary>I2: resolves each recorded file through <see cref="DependencyPaths.FromLedger"/> — an
    /// unsafe path is skipped rather than acted on. I3: a file whose live content no longer matches the
    /// recorded hash was replaced by the user or another tool and is left alone (dropped from the ledger
    /// regardless, since the whole entry is rebuilt/removed by the caller either way).</summary>
    private void DeleteEntryFiles(string gameMini, string pluginId, LedgerEntry entry)
    {
        foreach (var f in entry.Files)
        {
            var abs = DependencyPaths.FromLedger(gameMini, f.Path);
            if (abs is null) continue; // I2: an unsafe recorded path is never resolved or acted on
            if (DependencyFileHash.Matches(_fs, abs, f.Sha256)) _fs.File.Delete(abs);
            var parked = DependencyPaths.ParkedPath(gameMini, pluginId, f.Path);
            if (_fs.File.Exists(parked)) _fs.File.Delete(parked);
        }
    }
}

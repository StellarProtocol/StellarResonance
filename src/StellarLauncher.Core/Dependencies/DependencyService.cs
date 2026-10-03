using System;
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
        var results = new Dictionary<string, DependencyStatus>();
        foreach (var d in deps)
        {
            ct.ThrowIfCancellationRequested();
            var gate = GateStatus(d, skippedIds, results);
            if (gate is not null)
            {
                // I6: a skip's own Remove can fail on IO — that must surface as Failed, never throw.
                try
                {
                    if (gate.State == DependencyState.Skipped) Remove(gameMini, pluginId, d.Id);
                    results[d.Id] = gate;
                }
                catch (Exception ex) { results[d.Id] = new DependencyStatus(d.Id, DependencyState.Failed, ex.Message); }
                continue;
            }
            try { results[d.Id] = await EnsureOneAsync(gameMini, pluginId, d, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { results[d.Id] = new DependencyStatus(d.Id, DependencyState.Failed, ex.Message); }
        }
        return deps.Select(d => results[d.Id]).ToList();
    }

    public IReadOnlyList<DependencyStatus> Status(string gameMini, string pluginId,
        IReadOnlyList<PluginDependency> deps, ISet<string> skippedIds)
    {
        // Minor 3: Status is read-only — it must not have the side effect of quarantining a corrupt
        // ledger (it still reads one as empty, via Read's own fallback, either way).
        var ledger = _store.Read(gameMini, pluginId, quarantine: false);
        var results = new Dictionary<string, DependencyStatus>();
        foreach (var d in deps)
        {
            var gate = GateStatus(d, skippedIds, results);
            if (gate is not null) { results[d.Id] = gate; continue; }
            var entry = ledger.Entries.FirstOrDefault(e => e.DependencyId == d.Id);
            results[d.Id] = IsInstalled(gameMini, entry, d)
                ? new DependencyStatus(d.Id, DependencyState.Installed, null)
                : new DependencyStatus(d.Id, DependencyState.NotInstalled, null);
        }
        return deps.Select(d => results[d.Id]).ToList();
    }

    public void Remove(string gameMini, string pluginId, string dependencyId)
    {
        var ledger = _store.Read(gameMini, pluginId);
        var entry = ledger.Entries.FirstOrDefault(e => e.DependencyId == dependencyId);
        if (entry is null) return;
        DeleteEntryFiles(gameMini, pluginId, entry);
        _store.Write(gameMini, ledger with { Entries = ledger.Entries.Where(e => e.DependencyId != dependencyId).ToList() });
    }

    public void RemoveAll(string gameMini, string pluginId)
    {
        var ledger = _store.Read(gameMini, pluginId);
        foreach (var entry in ledger.Entries) DeleteEntryFiles(gameMini, pluginId, entry);
        _store.Write(gameMini, ledger with { Entries = Array.Empty<LedgerEntry>() });
    }

    public void ParkModdedOnly(string gameMini) => _parking.Park(gameMini);

    public void UnparkModdedOnly(string gameMini) => _parking.Unpark(gameMini);

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
            if (rs.State is DependencyState.Skipped or DependencyState.Blocked)
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

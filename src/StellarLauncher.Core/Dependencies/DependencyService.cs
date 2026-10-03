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
/// mechanics live in the <c>.Placement</c> partial; vanilla-launch parking in <see cref="DependencyParking"/>.</summary>
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
            if (skippedIds.Contains(d.Id) || (d.Requires ?? Array.Empty<string>()).Any(r => results.TryGetValue(r, out var rs) && rs.State != DependencyState.Installed))
            {
                Remove(gameMini, pluginId, d.Id);
                results[d.Id] = new DependencyStatus(d.Id, DependencyState.Skipped, null);
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
        var ledger = _store.Read(gameMini, pluginId);
        var results = new Dictionary<string, DependencyStatus>();
        foreach (var d in deps)
        {
            if (skippedIds.Contains(d.Id) || (d.Requires ?? Array.Empty<string>()).Any(r => results.TryGetValue(r, out var rs) && rs.State != DependencyState.Installed))
            {
                results[d.Id] = new DependencyStatus(d.Id, DependencyState.Skipped, null);
                continue;
            }
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

    private void DeleteEntryFiles(string gameMini, string pluginId, LedgerEntry entry)
    {
        foreach (var f in entry.Files)
        {
            var abs = _fs.Path.Combine(gameMini, f.Path);
            if (_fs.File.Exists(abs)) _fs.File.Delete(abs);
            var parked = DependencyPaths.ParkedPath(gameMini, pluginId, f.Path);
            if (_fs.File.Exists(parked)) _fs.File.Delete(parked);
        }
    }
}

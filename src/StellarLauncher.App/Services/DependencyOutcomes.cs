using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;

namespace StellarLauncher.App.Services;

/// <summary>The last install-time failure of each dependency, so the plugin page can show "Failed: …"
/// (which <see cref="IDependencyService.Status"/>, being read-only and offline, never reports).</summary>
public interface IDependencyOutcomes
{
    /// <summary>The Failed status the most recent <see cref="IDependencyService.EnsureAsync"/> returned for
    /// <paramref name="d"/> on this game folder, or null — also null once the declaration changed since
    /// (another version or checksum), or the dependency was installed/removed since.</summary>
    DependencyStatus? LastFailure(string gameMini, string pluginId, PluginDependency d);
}

/// <summary>Wraps the real <see cref="IDependencyService"/> and remembers, in memory for this launcher
/// session, the last EnsureAsync outcome of every dependency — whichever path ran it (launch review,
/// install/update, the page's re-tick). Everything else passes straight through.</summary>
public sealed class RecordingDependencyService : IDependencyService, IDependencyOutcomes
{
    private readonly IDependencyService _inner;
    private readonly object _gate = new();
    private readonly Dictionary<(string GameMini, string PluginId, string DepId), (string Version, string Sha256, DependencyStatus Status)> _failed = new();

    public RecordingDependencyService(IDependencyService inner) => _inner = inner;

    public async Task<IReadOnlyList<DependencyStatus>> EnsureAsync(string gameMini, string pluginId,
        IReadOnlyList<PluginDependency> deps, ISet<string> skippedIds, CancellationToken ct)
    {
        var results = await _inner.EnsureAsync(gameMini, pluginId, deps, skippedIds, ct);
        lock (_gate)
        {
            foreach (var s in results)
            {
                var key = (gameMini, pluginId, s.DependencyId);
                if (s.State == DependencyState.Failed && deps.FirstOrDefault(d => d.Id == s.DependencyId) is { } d)
                    _failed[key] = (d.Version, d.Sha256, s);
                else _failed.Remove(key);
            }
        }
        return results;
    }

    public DependencyStatus? LastFailure(string gameMini, string pluginId, PluginDependency d)
    {
        lock (_gate)
            return _failed.TryGetValue((gameMini, pluginId, d.Id), out var f) && f.Version == d.Version
                   && string.Equals(f.Sha256, d.Sha256, StringComparison.OrdinalIgnoreCase)
                ? f.Status : null;
    }

    public IReadOnlyList<DependencyStatus> Status(string gameMini, string pluginId, IReadOnlyList<PluginDependency> deps,
        ISet<string> skippedIds) => _inner.Status(gameMini, pluginId, deps, skippedIds);

    public void Remove(string gameMini, string pluginId, string dependencyId)
    {
        _inner.Remove(gameMini, pluginId, dependencyId);
        lock (_gate) _failed.Remove((gameMini, pluginId, dependencyId));
    }

    public void RemoveAll(string gameMini, string pluginId)
    {
        _inner.RemoveAll(gameMini, pluginId);
        lock (_gate)
            foreach (var k in _failed.Keys.Where(k => k.GameMini == gameMini && k.PluginId == pluginId).ToList()) _failed.Remove(k);
    }

    public void ParkModdedOnly(string gameMini) => _inner.ParkModdedOnly(gameMini);
    public void UnparkModdedOnly(string gameMini) => _inner.UnparkModdedOnly(gameMini);
    public IReadOnlyList<string> LedgerPluginIds(string gameMini) => _inner.LedgerPluginIds(gameMini);
}

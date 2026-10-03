using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;

namespace StellarLauncher.App.Services;

/// <summary>The last install-time problem (Failed or Blocked) of each dependency, so the plugin page can show
/// what <see cref="IDependencyService.Status"/>, being read-only and offline, can't see: a download/verify
/// failure, or a collision inside a zip directory mapping (known only once the archive is read).</summary>
public interface IDependencyOutcomes
{
    /// <summary>The Failed or Blocked status the most recent <see cref="IDependencyService.EnsureAsync"/> returned for
    /// <paramref name="d"/> on this game folder, or null — also null once the declaration changed since
    /// (another version or checksum), or the dependency was installed/removed since.</summary>
    DependencyStatus? LastProblem(string gameMini, string pluginId, PluginDependency d);
}

/// <summary>Wraps the real <see cref="IDependencyService"/> and remembers, in memory for this launcher
/// session, the last EnsureAsync problem (Failed/Blocked) of every dependency — whichever path ran it (launch review,
/// install/update, the page's re-tick). Everything else passes straight through.</summary>
public sealed class RecordingDependencyService : IDependencyService, IDependencyOutcomes
{
    private readonly IDependencyService _inner;
    private readonly object _gate = new();
    private readonly Dictionary<(string GameMini, string PluginId, string DepId), (string Version, string Sha256, DependencyStatus Status)> _problems = new();

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
                if (s.State is DependencyState.Failed or DependencyState.Blocked
                    && deps.FirstOrDefault(d => d.Id == s.DependencyId) is { } d)
                    _problems[key] = (d.Version, d.Sha256, s);
                else _problems.Remove(key);
            }
        }
        return results;
    }

    public DependencyStatus? LastProblem(string gameMini, string pluginId, PluginDependency d)
    {
        lock (_gate)
            return _problems.TryGetValue((gameMini, pluginId, d.Id), out var f) && f.Version == d.Version
                   && string.Equals(f.Sha256, d.Sha256, StringComparison.OrdinalIgnoreCase)
                ? f.Status : null;
    }

    public IReadOnlyList<DependencyStatus> Status(string gameMini, string pluginId, IReadOnlyList<PluginDependency> deps,
        ISet<string> skippedIds) => _inner.Status(gameMini, pluginId, deps, skippedIds);

    public async Task RemoveAsync(string gameMini, string pluginId, string dependencyId, CancellationToken ct = default)
    {
        await _inner.RemoveAsync(gameMini, pluginId, dependencyId, ct);
        lock (_gate) _problems.Remove((gameMini, pluginId, dependencyId));
    }

    public async Task RemoveAllAsync(string gameMini, string pluginId, CancellationToken ct = default)
    {
        await _inner.RemoveAllAsync(gameMini, pluginId, ct);
        lock (_gate)
            foreach (var k in _problems.Keys.Where(k => k.GameMini == gameMini && k.PluginId == pluginId).ToList()) _problems.Remove(k);
    }

    public Task ParkModdedOnlyAsync(string gameMini, CancellationToken ct = default) => _inner.ParkModdedOnlyAsync(gameMini, ct);
    public Task UnparkModdedOnlyAsync(string gameMini, IReadOnlySet<string>? keepParked = null, CancellationToken ct = default) =>
        _inner.UnparkModdedOnlyAsync(gameMini, keepParked, ct);
    public IReadOnlyList<string> LedgerPluginIds(string gameMini) => _inner.LedgerPluginIds(gameMini);
}

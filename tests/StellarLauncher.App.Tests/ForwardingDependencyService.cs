using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;

/// <summary>Passes every call to <paramref name="inner"/>; tests override the members they spy on or fault.</summary>
public class ForwardingDependencyService(IDependencyService inner) : IDependencyService
{
    public virtual Task<IReadOnlyList<DependencyStatus>> EnsureAsync(string gameMini, string pluginId, IReadOnlyList<PluginDependency> deps,
        ISet<string> skippedIds, CancellationToken ct) => inner.EnsureAsync(gameMini, pluginId, deps, skippedIds, ct);
    public virtual IReadOnlyList<DependencyStatus> Status(string gameMini, string pluginId, IReadOnlyList<PluginDependency> deps,
        ISet<string> skippedIds) => inner.Status(gameMini, pluginId, deps, skippedIds);
    public virtual Task RemoveAsync(string gameMini, string pluginId, string dependencyId, CancellationToken ct = default) =>
        inner.RemoveAsync(gameMini, pluginId, dependencyId, ct);
    public virtual Task RemoveAllAsync(string gameMini, string pluginId, CancellationToken ct = default) => inner.RemoveAllAsync(gameMini, pluginId, ct);
    public virtual Task ParkModdedOnlyAsync(string gameMini, CancellationToken ct = default) => inner.ParkModdedOnlyAsync(gameMini, ct);
    public virtual Task UnparkModdedOnlyAsync(string gameMini, IReadOnlySet<string>? keepParked = null, CancellationToken ct = default) =>
        inner.UnparkModdedOnlyAsync(gameMini, keepParked, ct);
    public virtual IReadOnlyList<string> LedgerPluginIds(string gameMini) => inner.LedgerPluginIds(gameMini);
    public virtual Task SetKeptAsync(string gameMini, string pluginId, bool kept, CancellationToken ct = default) =>
        inner.SetKeptAsync(gameMini, pluginId, kept, ct);
    public virtual bool IsKept(string gameMini, string pluginId) => inner.IsKept(gameMini, pluginId);
    public virtual Task RequestReinstallAsync(string gameMini, string pluginId, CancellationToken ct = default) =>
        inner.RequestReinstallAsync(gameMini, pluginId, ct);
    public virtual Task<bool> RemoveAllUnlessKeptAsync(string gameMini, string pluginId, CancellationToken ct = default) =>
        inner.RemoveAllUnlessKeptAsync(gameMini, pluginId, ct);
    public virtual IReadOnlyList<LedgerEntry> LedgerEntries(string gameMini, string pluginId) => inner.LedgerEntries(gameMini, pluginId);
}

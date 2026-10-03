using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Dependencies;

/// <summary>Where a single dependency ended up after <see cref="IDependencyService.EnsureAsync"/>/
/// <see cref="IDependencyService.Status"/> looked at it.</summary>
public enum DependencyState { NotInstalled, Installed, Skipped, Blocked, Failed }

/// <summary>One dependency's outcome. <paramref name="Detail"/> carries the reason for
/// <see cref="DependencyState.Blocked"/>/<see cref="DependencyState.Failed"/>, or null otherwise.</summary>
public sealed record DependencyStatus(string DependencyId, DependencyState State, string? Detail);

/// <summary>Downloads, verifies and places a plugin's declared dependencies — generically, without
/// knowing what any of them are (docs/manifest-standard.md § dependencies).</summary>
public interface IDependencyService
{
    /// <summary>Brings every dependency in <paramref name="deps"/> up to date: installs what's missing,
    /// leaves what's already current alone, and removes/skips anything in <paramref name="skippedIds"/>
    /// or whose <c>requires</c> chain isn't fully installed. Never throws except
    /// <see cref="System.OperationCanceledException"/> — every other failure comes back as a
    /// <see cref="DependencyState.Failed"/>/<see cref="DependencyState.Blocked"/> status.</summary>
    Task<IReadOnlyList<DependencyStatus>> EnsureAsync(string gameMini, string pluginId, IReadOnlyList<PluginDependency> deps,
        ISet<string> skippedIds, CancellationToken ct);

    /// <summary>Read-only equivalent of <see cref="EnsureAsync"/>: no network, no writes — just what the
    /// ledger and disk already say.</summary>
    IReadOnlyList<DependencyStatus> Status(string gameMini, string pluginId, IReadOnlyList<PluginDependency> deps, ISet<string> skippedIds);

    /// <summary>Deletes the files this dependency placed (and their parked copies), then its ledger entry.</summary>
    void Remove(string gameMini, string pluginId, string dependencyId);

    /// <summary>Deletes every dependency's files for this plugin, then its ledger.</summary>
    void RemoveAll(string gameMini, string pluginId);

    /// <summary>Moves every placed <c>moddedOnly</c> file (from every plugin's ledger) out of the game
    /// tree, for a vanilla launch. Idempotent.</summary>
    void ParkModdedOnly(string gameMini);

    /// <summary>Moves parked files back, unless something now occupies the destination — in which case
    /// it stays parked. Idempotent.</summary>
    void UnparkModdedOnly(string gameMini);

    /// <summary>The plugin ids that currently have a dependency ledger in this game folder (valid ids
    /// only; nothing is read or quarantined). Used to find ledgers left behind by a plugin that is no
    /// longer installed.</summary>
    IReadOnlyList<string> LedgerPluginIds(string gameMini);
}

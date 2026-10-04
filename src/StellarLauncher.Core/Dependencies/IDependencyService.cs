using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Dependencies;

/// <summary>Where a single dependency ended up after <see cref="IDependencyService.EnsureAsync"/>/
/// <see cref="IDependencyService.Status"/> looked at it.</summary>
public enum DependencyState { NotInstalled, Installed, Skipped, Blocked, Failed }

/// <summary>Final review M-d: WHY a Blocked or Skipped outcome is what it is, so the plugin page can say it plainly.
/// <see cref="OtherOwner"/>: another plugin's (or another dependency's) recorded file is at the destination;
/// <see cref="PlayerFile"/>: a file nobody's ledger owns (the player's), or one of ours the player changed;
/// <see cref="WaitingForPrerequisite"/>: Skipped only because a <c>requires</c> prerequisite is Blocked (or is
/// itself waiting) — <c>Detail</c> is then that prerequisite's id.</summary>
public enum DependencyReason { None, PlayerFile, OtherOwner, WaitingForPrerequisite }

/// <summary>One dependency's outcome. <paramref name="Detail"/> carries the reason for
/// <see cref="DependencyState.Blocked"/>/<see cref="DependencyState.Failed"/> (and, with
/// <see cref="DependencyReason.WaitingForPrerequisite"/>, the prerequisite's id), or null otherwise.</summary>
public sealed record DependencyStatus(string DependencyId, DependencyState State, string? Detail,
    DependencyReason Reason = DependencyReason.None);

/// <summary>Downloads, verifies and places a plugin's declared dependencies — generically, without
/// knowing what any of them are (docs/manifest-standard.md § dependencies).</summary>
/// <remarks>Every mutating member is serialised per game folder and only ever waits for that by awaiting
/// (fix round 2) — there are deliberately no synchronous mutating members, so no caller on a UI thread can
/// block it behind an install whose continuations need that thread.</remarks>
public interface IDependencyService
{
    /// <summary>Brings every dependency in <paramref name="deps"/> up to date: installs what's missing,
    /// leaves what's already current alone, and removes/skips anything in <paramref name="skippedIds"/>
    /// or whose <c>requires</c> chain isn't fully installed. <paramref name="deps"/> is the plugin's WHOLE
    /// declaration: anything its ledger still records that is not in it (dropped or renamed by an update; all
    /// of it when <paramref name="deps"/> is empty) is removed first. Never throws except
    /// <see cref="System.OperationCanceledException"/> — every other failure comes back as a
    /// <see cref="DependencyState.Failed"/>/<see cref="DependencyState.Blocked"/> status.</summary>
    Task<IReadOnlyList<DependencyStatus>> EnsureAsync(string gameMini, string pluginId, IReadOnlyList<PluginDependency> deps,
        ISet<string> skippedIds, CancellationToken ct);

    /// <summary>Read-only equivalent of <see cref="EnsureAsync"/>: no network, no writes — just what the
    /// ledger and disk already say.</summary>
    IReadOnlyList<DependencyStatus> Status(string gameMini, string pluginId, IReadOnlyList<PluginDependency> deps, ISet<string> skippedIds);

    /// <summary>Deletes the files this dependency placed (and their parked copies), then its ledger entry.</summary>
    Task RemoveAsync(string gameMini, string pluginId, string dependencyId, CancellationToken ct = default);

    /// <summary>Deletes every dependency's files for this plugin, then its ledger.</summary>
    Task RemoveAllAsync(string gameMini, string pluginId, CancellationToken ct = default);

    /// <summary>Moves every placed <c>moddedOnly</c> file (from every plugin's ledger) out of the game
    /// tree, for a vanilla launch. Idempotent.</summary>
    Task ParkModdedOnlyAsync(string gameMini, CancellationToken ct = default);

    /// <summary>Moves parked files back, unless something now occupies the destination — in which case
    /// it stays parked. A plugin in <paramref name="keepParked"/> (one that is disabled — final review I6) is
    /// treated as for a vanilla launch instead: its placed <c>moddedOnly</c> files are parked, and restored by
    /// a later call that no longer lists it. Idempotent.</summary>
    Task UnparkModdedOnlyAsync(string gameMini, IReadOnlySet<string>? keepParked = null, CancellationToken ct = default);

    /// <summary>The plugin ids that currently have a dependency ledger in this game folder (valid ids
    /// only; nothing is read or quarantined). Used to find ledgers left behind by a plugin that is no
    /// longer installed.</summary>
    IReadOnlyList<string> LedgerPluginIds(string gameMini);

    /// <summary>v3 V3: marks (<paramref name="kept"/> true) or un-marks this plugin's ledger as KEPT — the plugin was
    /// removed but its dependencies stay launcher-managed: still parked for Vanilla launches, skipped by the orphan
    /// sweep, adopted again by the next <see cref="EnsureAsync"/>. A flag write only — no file moves, so it is safe while
    /// files are parked. No-op without a ledger or for an invalid id; a present-but-unreadable ledger throws
    /// <see cref="System.InvalidOperationException"/> and nothing is written.</summary>
    Task SetKeptAsync(string gameMini, string pluginId, bool kept, CancellationToken ct = default);

    /// <summary>v3 V3: read-only (no gate, never quarantines): whether this plugin's ledger is marked kept. False when
    /// there is no readable ledger or the id is invalid.</summary>
    bool IsKept(string gameMini, string pluginId);

    /// <summary>v3 V2: the next <see cref="EnsureAsync"/> for this plugin removes the files of every declared, non-skipped
    /// dependency (hash-checked — a file the player changed is left, and then reads Blocked) and downloads + verifies
    /// them afresh. A flag write only (safe while parked, so a Vanilla client defers it to the next Modded launch).
    /// No-op without a ledger; throws like <see cref="SetKeptAsync"/>.</summary>
    Task RequestReinstallAsync(string gameMini, string pluginId, CancellationToken ct = default);
}

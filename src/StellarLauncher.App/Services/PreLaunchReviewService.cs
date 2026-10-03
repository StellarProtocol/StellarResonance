using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.App.ViewModels;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.Services;

/// <summary>Per-client pre-launch review: classify this client's plugins against the framework that will
/// run, show the existing dialog only when there is something to do. Fail-open offline (as today).</summary>
public sealed class PreLaunchReviewService : IPreLaunchReview
{
    private readonly RegistryCache _registry;
    private readonly ClientInventory _inventory;
    private readonly IVersionService _versions;
    private readonly PluginInstallDeps _deps;
    private readonly Func<PreLaunchReviewViewModel, Task<PreLaunchResult>> _prompt;

    public PreLaunchReviewService(RegistryCache registry, ClientInventory inventory, IVersionService versions,
        PluginInstallDeps deps, Func<PreLaunchReviewViewModel, Task<PreLaunchResult>> prompt)
    {
        _registry = registry; _inventory = inventory; _versions = versions; _deps = deps; _prompt = prompt;
    }

    public Task<bool> ReviewAsync(ClientProfile client, CancellationToken ct) => ReviewAsync(client, _ => { }, ct);

    /// <param name="status">The client's state line during the review (Task 6 d): dependency downloads
    /// report "Preparing &lt;plugin&gt;: &lt;dependency&gt;…"; the caller clears it when the review ends.</param>
    public async Task<bool> ReviewAsync(ClientProfile client, Action<string?> status, CancellationToken ct)
    {
        if (!client.Modded) { await TryParkAsync(client, ct); return true; }
        try
        {
            // Dependencies must be back in place before anything below reads or installs plugins — no
            // EnsureAsync may ever run while a modded-only file is still parked for a vanilla launch.
            // Fix round 1, Minor 1: fail-open on its own (like TryPark) — an unpark failure must not skip
            // the rest of the review (registry check, dialog, ensure). Final review I6: a disabled plugin's
            // moddedOnly files stay parked (DependencyRunner.RestoreForModdedAsync).
            var parkedForDisabled = await DependencyRunner.RestoreForModdedAsync(_deps, client.GameMiniDir, ct);

            var registry = await _registry.ForChannelAsync(client.Channel, ct);
            var manifest = await _versions.FetchAsync(ChannelManifests.FrameworkVersion(client.Channel), ct);

            var inv = _inventory.Read(client, registry);
            var target = manifest?.Versions.FirstOrDefault(v => v.Version == manifest.Latest);
            var installed = inv.Plugins.Select(p => new InstalledPluginInfo(p.Entry, p.Installed, p.Version)).ToList();
            var plan = PreLaunchPlanner.Build(inv.FrameworkVersion, target, AppInfo.LauncherVersion, installed, client.AutoUpdateBeforeLaunch);
            if (plan.IsEmpty)
            {
                await EnsureDependenciesAsync(client, registry, status, disabledBeforeDialog: null, ct);
                return true;
            }

            var vm = new PreLaunchReviewViewModel(inv.FrameworkVersion, target, AppInfo.LauncherVersion, installed, registry,
                client.AutoUpdateBeforeLaunch, client.GameMiniDir, _deps.Installer, _deps.Plugins, _deps.Http);
            if (await _prompt(vm) == PreLaunchResult.Cancel) return false;
            await EnsureDependenciesAsync(client, registry, status, parkedForDisabled, ct);
            return true;
        }
        // Fix round 1, Important 1: HttpClient's own request timeout throws OperationCanceledException
        // (as TaskCanceledException) too, independent of OUR ct — a cancelled review never answers
        // "proceed", but only when the CALLER actually asked to cancel; an unrelated OCE (a timeout
        // somewhere in registry/manifest fetch) must fail open just like any other exception, never
        // unwind as a silent "don't launch" (ClientSessions reads an escaping OCE as a user cancel).
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        // Fail-open over the WHOLE review path (unpark, registry/manifest offline, inventory read,
        // planner, dialog, an uncancelled OCE): the pre-launch review is advisory and must never trap
        // the player out of their game.
        catch (Exception) { return true; }
    }

    /// <summary>The dialog may have installed/updated/disabled plugins, so this re-reads the inventory
    /// rather than reusing the plan's — then removes dependencies left by plugins that are gone (Task 6 b)
    /// and brings every installed plugin's dependencies up to date, reporting progress on the state line
    /// and writing each resulting status line to the launcher log (Task 6 c). Swallows any non-cancel
    /// failure (including an HttpClient timeout surfacing as an uncancelled OperationCanceledException —
    /// fix round 1, Important 1): a dependency problem is surfaced on its own plugin page, never here, and
    /// must never be the reason a launch doesn't proceed.</summary>
    /// <param name="disabledBeforeDialog">Set when the dialog ran: it may have DISABLED plugins (it never enables
    /// one), so a newly disabled plugin's moddedOnly files are parked now (final review I6).</param>
    private async Task EnsureDependenciesAsync(ClientProfile client, IReadOnlyList<PluginEntry> registry,
        Action<string?> status, IReadOnlySet<string>? disabledBeforeDialog, CancellationToken ct)
    {
        try
        {
            var inv = _inventory.Read(client, registry);
            await SweepOrphansAsync(client, inv.Plugins.Where(p => p.Installed || p.Disabled).Select(p => p.Entry.Id), ct);
            if (disabledBeforeDialog is not null)
                await DependencyRunner.RestoreForModdedAsync(_deps, client.GameMiniDir, ct, unlessStillDisabled: disabledBeforeDialog);
            var installed = inv.Plugins.Where(p => p.Installed && p.Version is not null)
                .Select(p => (p.Entry, p.Version!)).ToList();
            var lines = await DependencyRunner.EnsureForClientAsync(_deps.Dependencies, client, installed, ct, t => status(t));
            foreach (var line in lines)
                if (line.IsProblem) DependencyLog.Failure(client.Name, line.Text); else Log(client, line.Text);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        // Fail-open — launch is never blocked by a dependency problem; M-f: one always-on line says so.
        catch (Exception ex) { DependencyLog.Failure(client.Name, $"dependencies could not be prepared — {ex.Message}"); }
    }

    /// <summary>Task 6 (b): a ledger whose plugin is no longer installed on this client (removed by hand,
    /// or its own cleanup failed) has its files removed. A plugin counts as present when the registry
    /// inventory sees it (installed or disabled) OR its folder still holds a version marker / sits under
    /// plugins-disabled — so a plugin missing from the registry never loses its dependencies. Each ledger
    /// is fail-open on its own.</summary>
    private async Task SweepOrphansAsync(ClientProfile client, IEnumerable<string> presentIds, CancellationToken ct)
    {
        // Id case: ledger stems are the registry id EnsureAsync was given, so ordinal matches; only a registry id
        // whose CASE changed would read as orphaned, and on Windows the folder-path checks below still find it.
        var present = presentIds.ToHashSet(StringComparer.Ordinal);
        foreach (var id in _deps.Dependencies.LedgerPluginIds(client.GameMiniDir))
        {
            try
            {
                if (present.Contains(id) || _deps.Plugins.IsInstalled(client.GameMiniDir, id)
                    || _deps.Plugins.IsDisabled(client.GameMiniDir, id)) continue;
                await _deps.Dependencies.RemoveAllAsync(client.GameMiniDir, id, ct);
                Log(client, $"{id}: plugin no longer installed — its dependencies were removed");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { DependencyLog.Failure(client.Name, $"{id}: orphaned dependencies could not be removed — {ex.Message}"); }
        }
    }

    /// <summary>The launcher's log (stellar-launcher.log when debug logging is on — see Program.cs).</summary>
    private static void Log(ClientProfile client, string line) => Trace.WriteLine($"[deps] {client.Name}: {line}");

    /// <summary>Vanilla launches park every moddedOnly dependency file out of the game tree. Best-effort —
    /// launch is never blocked by this.</summary>
    private async Task TryParkAsync(ClientProfile client, CancellationToken ct)
    {
        try { await _deps.Dependencies.ParkModdedOnlyAsync(client.GameMiniDir, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { DependencyLog.Failure(client.Name, $"parking for a vanilla launch failed — {ex.Message}"); /* fail-open */ }
    }
}

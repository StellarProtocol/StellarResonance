using System;
using System.Collections.Generic;
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

    public async Task<bool> ReviewAsync(ClientProfile client, CancellationToken ct)
    {
        if (!client.Modded) { TryPark(client); return true; }
        try
        {
            // Dependencies must be back in place before anything below reads or installs plugins — no
            // EnsureAsync may ever run while a modded-only file is still parked for a vanilla launch.
            _deps.Dependencies.UnparkModdedOnly(client.GameMiniDir);

            var registry = await _registry.ForChannelAsync(client.Channel, ct);
            var manifest = await _versions.FetchAsync(ChannelManifests.FrameworkVersion(client.Channel), ct);

            var inv = _inventory.Read(client, registry);
            var target = manifest?.Versions.FirstOrDefault(v => v.Version == manifest.Latest);
            var installed = inv.Plugins.Select(p => new InstalledPluginInfo(p.Entry, p.Installed, p.Version)).ToList();
            var plan = PreLaunchPlanner.Build(inv.FrameworkVersion, target, AppInfo.LauncherVersion, installed, client.AutoUpdateBeforeLaunch);
            if (plan.IsEmpty)
            {
                await EnsureDependenciesAsync(client, registry, ct);
                return true;
            }

            var vm = new PreLaunchReviewViewModel(inv.FrameworkVersion, target, AppInfo.LauncherVersion, installed, registry,
                client.AutoUpdateBeforeLaunch, client.GameMiniDir, _deps.Installer, _deps.Plugins, _deps.Http);
            if (await _prompt(vm) == PreLaunchResult.Cancel) return false;
            await EnsureDependenciesAsync(client, registry, ct);
            return true;
        }
        catch (OperationCanceledException) { throw; }   // a cancelled review never answers "proceed"
        // Fail-open over the WHOLE review path (unpark, registry/manifest offline, inventory read,
        // planner, dialog): the pre-launch review is advisory and must never trap the player out of
        // their game.
        catch (Exception) { return true; }
    }

    /// <summary>The dialog may have installed/updated/disabled plugins, so this re-reads the inventory
    /// rather than reusing the plan's — then brings every installed plugin's dependencies up to date.
    /// Swallows any non-cancel failure: a dependency problem is surfaced on its own plugin page, never
    /// here, and must never be the reason a launch doesn't proceed.</summary>
    private async Task EnsureDependenciesAsync(ClientProfile client, IReadOnlyList<PluginEntry> registry, CancellationToken ct)
    {
        try
        {
            var inv = _inventory.Read(client, registry);
            var installed = inv.Plugins.Where(p => p.Installed && p.Version is not null)
                .Select(p => (p.Entry, p.Version!)).ToList();
            await DependencyRunner.EnsureForClientAsync(_deps.Dependencies, client, installed, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { /* fail-open — launch is never blocked by a dependency problem */ }
    }

    /// <summary>Vanilla launches park every moddedOnly dependency file out of the game tree. Best-effort —
    /// launch is never blocked by this.</summary>
    private void TryPark(ClientProfile client)
    {
        try { _deps.Dependencies.ParkModdedOnly(client.GameMiniDir); }
        catch { /* fail-open */ }
    }
}

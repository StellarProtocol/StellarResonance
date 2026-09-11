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
        if (!client.Modded) return true;
        IReadOnlyList<PluginEntry> registry;
        FrameworkManifest? manifest;
        try
        {
            registry = await _registry.ForChannelAsync(client.Channel, ct);
            manifest = await _versions.FetchAsync(ChannelManifests.FrameworkVersion(client.Channel), ct);
        }
        catch (Exception) { return true; }   // offline — never trap the player

        var inv = _inventory.Read(client, registry);
        var target = manifest.Versions.FirstOrDefault(v => v.Version == manifest.Latest);
        var installed = inv.Plugins.Select(p => new InstalledPluginInfo(p.Entry, p.Installed, p.Version)).ToList();
        var plan = PreLaunchPlanner.Build(inv.FrameworkVersion, target, AppInfo.LauncherVersion, installed, client.AutoUpdateBeforeLaunch);
        if (plan.IsEmpty) return true;

        var vm = new PreLaunchReviewViewModel(inv.FrameworkVersion, target, AppInfo.LauncherVersion, installed, registry,
            client.AutoUpdateBeforeLaunch, client.GameMiniDir, _deps.Installer, _deps.Plugins, _deps.Http);
        return await _prompt(vm) != PreLaunchResult.Cancel;
    }
}

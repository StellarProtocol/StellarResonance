using System;
using System.Threading.Tasks;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Model;

namespace StellarLauncher.App.Services;

/// <summary>An install / update / reinstall click (v3 V1/V2): the client, the plugin, the version to put in place, and
/// what is there now.</summary>
public sealed record PluginInstallRequest(ClientProfile Client, PluginEntry Entry, PluginVersion Version, bool Installed, string? InstalledVersion)
{
    /// <summary>The same version again, or a present copy without a version marker (adopted).</summary>
    public bool IsReinstall => Installed && (InstalledVersion is null || InstalledVersion == Version.Version);
}

/// <summary>v3 (spec § 12): the ONE path every user-started install takes — the dashboard matrix, the plugins-tab rows and
/// the plugin page. Bulk paths (copy a plugin set, the pre-launch updates) call <see cref="PluginDownloads"/> directly
/// and show no step.</summary>
public static class PluginInstallFlow
{
    /// <summary>True when the click opens a step (the plugin page then skips its inline Confirm — the step IS it).</summary>
    public static bool NeedsStep(PluginInstallRequest r) => r.IsReinstall
        ? PluginStepBuilder.Reinstall(r.Client, r.Entry, r.Version) is not null
        : PluginStepBuilder.OfferedOptional(r.Entry, r.Version, r.InstalledVersion).Count > 0;

    /// <summary>Asks the step when there is one, then installs. False = the player cancelled: nothing was downloaded,
    /// installed or recorded. V1: the choice is written to SkippedDependencies and SAVED before anything downloads, so an
    /// unticked dependency is never fetched. V2: "Also reinstall dependencies" reaches PluginDownloads.</summary>
    public static async Task<bool> RunAsync(PluginInstallDeps deps, IPluginSteps steps, PluginInstallRequest r,
        Action saveProfile, Action<string>? status)
    {
        var reinstallDependencies = false;
        if (r.IsReinstall)
        {
            if (PluginStepBuilder.Reinstall(r.Client, r.Entry, r.Version) is { } step)
            {
                if (await steps.AskReinstallAsync(step) is not { } also) return false;
                reinstallDependencies = also;
            }
        }
        else if (PluginStepBuilder.OfferedOptional(r.Entry, r.Version, r.InstalledVersion) is { Count: > 0 } offered)
        {
            if (await steps.AskInstallAsync(PluginStepBuilder.Install(r.Entry, r.Version, offered, r.Client)) is not { } choice) return false;
            PluginStepBuilder.ApplyInstallChoice(r.Client, r.Entry.Id, offered, choice.Unticked);
            saveProfile();
        }
        await PluginDownloads.InstallAsync(deps, r.Client, r.Entry, r.Version, status, reinstallDependencies);
        return true;
    }
}

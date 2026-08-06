using System.Collections.Generic;
using System.Linq;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Services;

/// <summary>Pure classification of installed plugins against the framework that will actually run.
/// See docs/superpowers/specs/2026-08-06-launcher-prelaunch-autoupdate-design.md.</summary>
public static class PreLaunchPlanner
{
    public static PreLaunchPlan Build(
        string? installedFramework,
        VersionManifest? latest,
        string launcherVersion,
        IReadOnlyList<InstalledPluginInfo> plugins,
        bool applyFrameworkUpdate)
    {
        var fwAction = FrameworkPlanAction.None;
        string? fwTarget = null;
        if (installedFramework is not null && latest is not null
            && VersionService.IsNewer(latest.Version, installedFramework))
        {
            fwTarget = latest.Version;
            fwAction = VersionService.LauncherSupported(latest.MinLauncherVersion, launcherVersion)
                ? FrameworkPlanAction.UpdateAvailable
                : FrameworkPlanAction.UpdateBlockedByLauncher;
        }

        var effective = (applyFrameworkUpdate && fwAction == FrameworkPlanAction.UpdateAvailable)
            ? fwTarget : installedFramework;

        var items = plugins.Where(p => p.Installed).Select(p => Classify(p, effective)).ToList();
        return new PreLaunchPlan(fwAction, fwTarget, effective, items);
    }

    private static PluginPlanItem Classify(InstalledPluginInfo p, string? effectiveFramework)
    {
        var e = p.Entry;
        // Newest registry version that runs on the effective framework (Versions is newest-first).
        var best = effectiveFramework is null ? null
            : e.Versions.FirstOrDefault(v =>
                VersionService.IsModSystemCompatible(effectiveFramework, v.MinModSystemVersion, v.MaxModSystemVersion));

        PluginPlanItem Item(string? target, PluginPlanStatus s) =>
            new(e.Id, e.Name, p.InstalledVersion, target, s);

        if (p.InstalledVersion is null)   // adopted / no marker: reinstall to a known-compatible build
            return best is not null ? Item(best.Version, PluginPlanStatus.RequiredCompatFix)
                                    : Item(null, PluginPlanStatus.UnfixableIncompatible);

        var installedRec = e.Versions.FirstOrDefault(v => v.Version == p.InstalledVersion);
        var installedCompatible = effectiveFramework is not null && installedRec is not null
            && VersionService.IsModSystemCompatible(effectiveFramework, installedRec.MinModSystemVersion, installedRec.MaxModSystemVersion);

        if (installedCompatible)
            return best is not null && VersionService.IsNewer(best.Version, p.InstalledVersion)
                ? Item(best.Version, PluginPlanStatus.UpdateAvailable)
                : Item(null, PluginPlanStatus.UpToDate);

        // Provably incompatible, or its record is gone (can't confirm it's safe) → require the fix.
        return best is not null ? Item(best.Version, PluginPlanStatus.RequiredCompatFix)
                                : Item(null, PluginPlanStatus.UnfixableIncompatible);
    }
}

using System.Collections.Generic;
using System.Linq;

namespace StellarLauncher.Core.Model;

public enum PluginPlanStatus { UpToDate, UpdateAvailable, RequiredCompatFix, UnfixableIncompatible }

public enum FrameworkPlanAction { None, UpdateAvailable, UpdateBlockedByLauncher }

/// <summary>An installed, registry-known plugin fed to the planner.</summary>
public sealed record InstalledPluginInfo(PluginEntry Entry, bool Installed, string? InstalledVersion);

public sealed record PluginPlanItem(
    string Id, string Name, string? InstalledVersion, string? TargetVersion, PluginPlanStatus Status)
{
    public bool NeedsUpdate => Status is PluginPlanStatus.UpdateAvailable or PluginPlanStatus.RequiredCompatFix;
    /// <summary>Enabled + can't run: launch is blocked until updated or disabled.</summary>
    public bool Blocks => Status is PluginPlanStatus.RequiredCompatFix or PluginPlanStatus.UnfixableIncompatible;
}

public sealed record PreLaunchPlan(
    FrameworkPlanAction FrameworkAction,
    string? FrameworkTarget,
    string? EffectiveFramework,
    IReadOnlyList<PluginPlanItem> Items)
{
    /// <summary>Nothing to review → launch directly. (A launcher-blocked framework update with all
    /// plugins fine is not "actionable" here — the home banner already surfaces it.)</summary>
    public bool IsEmpty => FrameworkAction != FrameworkPlanAction.UpdateAvailable
        && Items.All(i => i.Status == PluginPlanStatus.UpToDate);
    public bool HasBlockers => Items.Any(i => i.Status == PluginPlanStatus.UnfixableIncompatible);
}

using System.Collections.Generic;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;
using Xunit;

public class PreLaunchPlannerTests
{
    private static PluginVersion V(string ver, string min, string? max = null) =>
        new(ver, "2026-01-01", $"P.dll", $"https://x/{ver}.dll", "sha", min, max, null);

    private static PluginEntry Entry(string id, params PluginVersion[] versions) =>
        new(id, id, "", "Stellar", versions);   // versions newest-first

    private static VersionManifest Fw(string ver, string minLauncher = "1.0.0") =>
        new(ver, "2026-01-01", "https://x/fw.zip", "sha", minLauncher,
            new Changelog(new string[0], new string[0], new string[0], new string[0]));

    // The concrete owner scenario: framework 1.17.0 installed, CombatMeter 1.6.0 (cap 1.17.0),
    // framework 1.18.0 available, registry has CombatMeter 1.6.1 (min 1.18.0).
    [Fact]
    public void Framework_update_forces_compat_plugin_update()
    {
        var cm = Entry("combatmeter", V("1.6.1", "1.18.0"), V("1.6.0", "1.17.0", "1.17.0"));
        var plan = PreLaunchPlanner.Build("1.17.0", Fw("1.18.0"), "1.3.0",
            new[] { new InstalledPluginInfo(cm, true, "1.6.0") }, applyFrameworkUpdate: true);

        Assert.Equal(FrameworkPlanAction.UpdateAvailable, plan.FrameworkAction);
        Assert.Equal("1.18.0", plan.EffectiveFramework);
        var item = Assert.Single(plan.Items);
        Assert.Equal(PluginPlanStatus.RequiredCompatFix, item.Status);
        Assert.Equal("1.6.1", item.TargetVersion);
        Assert.False(plan.IsEmpty);
    }

    [Fact]
    public void Not_applying_framework_update_judges_against_installed_and_plugin_is_up_to_date()
    {
        var cm = Entry("combatmeter", V("1.6.1", "1.18.0"), V("1.6.0", "1.17.0", "1.17.0"));
        var plan = PreLaunchPlanner.Build("1.17.0", Fw("1.18.0"), "1.3.0",
            new[] { new InstalledPluginInfo(cm, true, "1.6.0") }, applyFrameworkUpdate: false);

        Assert.Equal("1.17.0", plan.EffectiveFramework);
        Assert.Equal(PluginPlanStatus.UpToDate, Assert.Single(plan.Items).Status);   // 1.6.0 runs on 1.17.0; 1.6.1 needs 1.18.0
    }

    [Fact]
    public void Newer_compatible_version_is_update_available()
    {
        var e = Entry("hud", V("1.3.0", "1.0.0"), V("1.2.0", "1.0.0"));
        var plan = PreLaunchPlanner.Build("1.18.0", null, "1.3.0",
            new[] { new InstalledPluginInfo(e, true, "1.2.0") }, applyFrameworkUpdate: false);

        var item = Assert.Single(plan.Items);
        Assert.Equal(PluginPlanStatus.UpdateAvailable, item.Status);
        Assert.Equal("1.3.0", item.TargetVersion);
    }

    [Fact]
    public void Incompatible_with_no_compatible_version_is_unfixable_and_blocks()
    {
        var e = Entry("old", V("0.9.0", "1.0.0", "1.17.0"));   // capped at 1.17.0, nothing newer
        var plan = PreLaunchPlanner.Build("1.18.0", null, "1.3.0",
            new[] { new InstalledPluginInfo(e, true, "0.9.0") }, applyFrameworkUpdate: false);

        var item = Assert.Single(plan.Items);
        Assert.Equal(PluginPlanStatus.UnfixableIncompatible, item.Status);
        Assert.True(plan.HasBlockers);
    }

    [Fact]
    public void Unknown_installed_version_reinstalls_to_newest_compatible()
    {
        var e = Entry("adopted", V("2.0.0", "1.0.0"));
        var plan = PreLaunchPlanner.Build("1.18.0", null, "1.3.0",
            new[] { new InstalledPluginInfo(e, true, null) }, applyFrameworkUpdate: false);

        var item = Assert.Single(plan.Items);
        Assert.Equal(PluginPlanStatus.RequiredCompatFix, item.Status);
        Assert.Equal("2.0.0", item.TargetVersion);
    }

    [Fact]
    public void Launcher_too_old_blocks_framework_update_and_judges_against_installed()
    {
        var cm = Entry("combatmeter", V("1.6.1", "1.18.0"), V("1.6.0", "1.17.0", "1.17.0"));
        var plan = PreLaunchPlanner.Build("1.17.0", Fw("1.18.0", minLauncher: "9.9.9"), "1.3.0",
            new[] { new InstalledPluginInfo(cm, true, "1.6.0") }, applyFrameworkUpdate: true);

        Assert.Equal(FrameworkPlanAction.UpdateBlockedByLauncher, plan.FrameworkAction);
        Assert.Equal("1.17.0", plan.EffectiveFramework);   // can't apply → judge against installed
        Assert.Equal(PluginPlanStatus.UpToDate, Assert.Single(plan.Items).Status);
    }

    [Fact]
    public void Not_installed_plugins_are_ignored()
    {
        var e = Entry("x", V("1.0.0", "1.0.0"));
        var plan = PreLaunchPlanner.Build("1.18.0", null, "1.3.0",
            new[] { new InstalledPluginInfo(e, false, null) }, applyFrameworkUpdate: false);
        Assert.Empty(plan.Items);
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void Unknown_version_with_no_compatible_is_unfixable()
    {
        var e = Entry("adopted", V("0.9.0", "1.0.0", "1.17.0"));   // only version, capped at 1.17.0
        var plan = PreLaunchPlanner.Build("1.18.0", null, "1.3.0",
            new[] { new InstalledPluginInfo(e, true, null) }, applyFrameworkUpdate: false);

        var item = Assert.Single(plan.Items);
        Assert.Equal(PluginPlanStatus.UnfixableIncompatible, item.Status);
        Assert.True(plan.HasBlockers);
    }

    [Fact]
    public void Installed_version_absent_from_registry_forces_fix()
    {
        var cm = Entry("combatmeter", V("1.6.1", "1.18.0"), V("1.6.0", "1.17.0", "1.17.0"));
        var plan = PreLaunchPlanner.Build("1.18.0", null, "1.3.0",
            new[] { new InstalledPluginInfo(cm, true, "1.5.0") }, applyFrameworkUpdate: false);  // 1.5.0 not in registry

        var item = Assert.Single(plan.Items);
        Assert.Equal(PluginPlanStatus.RequiredCompatFix, item.Status);
        Assert.Equal("1.6.1", item.TargetVersion);
    }
}

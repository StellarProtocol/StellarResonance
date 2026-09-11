using System;
using System.Collections.Generic;
using System.Linq;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Matrix;
using StellarLauncher.Core.Model;
using Xunit;

public class CopySetPlannerTests
{
    private static PluginEntry Entry(string id, string min, params string[] versions)
        => new(id, id, "d", "a", versions.Select(v => new PluginVersion(v, null, $"{id}.dll", $"https://cdn/{id}.dll", "sha", min, null, null)).ToList());

    private static readonly PluginEntry Cm = Entry("combatmeter", "2.0.0", "2.10.0");
    private static readonly PluginEntry Ph = Entry("playerhud", "2.0.0", "2.1.0");
    private static readonly PluginEntry Wd = Entry("wardrobe", "2.6.4", "1.3.0");
    private static readonly PluginEntry Cb = Entry("cooldownbar", "2.0.0", "2.2.2");
    private static readonly IReadOnlyList<PluginEntry> Reg = new[] { Cm, Ph, Wd, Cb };

    private static ClientColumn Col(string id, string? fw, params InstalledPlugin[] plugins)
        => new(new ClientProfile { Id = id, Name = id }, new InventorySnapshot(true, fw, true, plugins, Array.Empty<DuplicateSlot>(), 0), Reg);

    [Fact]
    public void Installs_only_what_target_lacks_and_can_run()
    {
        var source = Col("main", "2.8.0",
            new InstalledPlugin(Cm, true, "2.10.0", false),
            new InstalledPlugin(Ph, true, "2.1.0", false),
            new InstalledPlugin(Wd, true, "1.3.0", false),
            new InstalledPlugin(Cb, false, "2.2.2", true));     // disabled on source
        var target = Col("test", "2.5.0",
            new InstalledPlugin(Cm, true, "2.10.0", false));

        var plan = CopySetPlanner.Plan(source, target);

        Assert.Equal(CopyStatus.AlreadyInstalled, plan.Single(i => i.Entry.Id == "combatmeter").Status);
        Assert.Equal(CopyStatus.Install, plan.Single(i => i.Entry.Id == "playerhud").Status);
        Assert.Equal("2.1.0", plan.Single(i => i.Entry.Id == "playerhud").TargetVersion);
        Assert.Equal(CopyStatus.NoCompatibleVersion, plan.Single(i => i.Entry.Id == "wardrobe").Status);   // target fw 2.5.0 < 2.6.4
        Assert.Equal(CopyStatus.SkippedDisabledOnSource, plan.Single(i => i.Entry.Id == "cooldownbar").Status);
        Assert.Equal(4, plan.Count);
    }

    [Fact]
    public void Target_without_framework_gets_NoFramework_for_everything_missing()
    {
        var source = Col("main", "2.8.0", new InstalledPlugin(Ph, true, "2.1.0", false));
        var target = Col("asia", null);
        Assert.Equal(CopyStatus.NoFramework, CopySetPlanner.Plan(source, target).Single().Status);
    }
}

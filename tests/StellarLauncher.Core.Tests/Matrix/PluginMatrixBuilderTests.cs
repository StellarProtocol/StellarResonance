using System;
using System.Collections.Generic;
using System.Linq;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Matrix;
using StellarLauncher.Core.Model;
using Xunit;

public class PluginMatrixBuilderTests
{
    private static PluginEntry Entry(string id, string name, string min, params string[] versions)
        => new(id, name, "d", "a", versions.Select(v => new PluginVersion(v, null, $"{id}.dll", $"https://cdn/{id}.dll", "sha", min, null, null)).ToList());

    private static readonly PluginEntry Cm = Entry("combatmeter", "CombatMeter", "2.0.0", "2.10.0", "2.9.1");
    private static readonly PluginEntry Mn = Entry("minimalnameplate", "Minimal Nameplate", "2.0.0", "2.1.2", "2.1.1");
    private static readonly PluginEntry Wd = Entry("wardrobe", "Wardrobe", "2.6.4", "1.3.0");
    private static readonly PluginEntry Rc = Entry("rc-only", "RC Only", "2.0.0", "0.9.0-rc.1");
    private static readonly IReadOnlyList<PluginEntry> Stable = new[] { Cm, Mn, Wd };
    private static readonly IReadOnlyList<PluginEntry> Testing = new[] { Cm, Mn, Wd, Rc };

    private static InventorySnapshot Inv(string? fw, params InstalledPlugin[] plugins)
        => new(true, fw, true, plugins, Array.Empty<DuplicateSlot>(), 0);

    private static ClientColumn Col(string id, string? fw, IReadOnlyList<PluginEntry> reg, params InstalledPlugin[] plugins)
        => new(new ClientProfile { Id = id, Name = id, GameMiniDir = "/" + id }, Inv(fw, plugins), reg);

    [Fact]
    public void Classifies_every_cell_kind()
    {
        var main = Col("main", "2.8.0", Stable,
            new InstalledPlugin(Cm, true, "2.10.0", false),
            new InstalledPlugin(Mn, true, "2.1.2", false),
            new InstalledPlugin(Wd, false, "1.3.0", true));
        var test = Col("test", "2.7.4", Testing,
            new InstalledPlugin(Cm, true, "2.10.0", false),
            new InstalledPlugin(Mn, true, "2.1.1", false));
        var asia = Col("asia", null, Stable);
        var old = Col("old", "2.5.0", Stable, new InstalledPlugin(Wd, true, "1.3.0", false));

        var m = PluginMatrixBuilder.Build(new[] { main, test, asia, old });

        Assert.Equal(new[] { "CombatMeter", "Minimal Nameplate", "RC Only", "Wardrobe" }, m.Rows.Select(r => r.Name).ToArray());
        MatrixCell Cell(string plugin, string client) => m.Rows.Single(r => r.PluginId == plugin).Cells.Single(c => c.ClientId == client);

        Assert.Equal(MatrixCellKind.Installed, Cell("combatmeter", "main").Kind);
        Assert.Equal(MatrixCellKind.UpdateAvailable, Cell("minimalnameplate", "test").Kind);
        Assert.Equal("2.1.2", Cell("minimalnameplate", "test").TargetVersion);
        Assert.Equal(MatrixCellKind.Disabled, Cell("wardrobe", "main").Kind);
        Assert.Equal(MatrixCellKind.NotInstalled, Cell("wardrobe", "test").Kind);
        Assert.Equal("1.3.0", Cell("wardrobe", "test").TargetVersion);
        Assert.Equal(MatrixCellKind.NeedsFramework, Cell("combatmeter", "asia").Kind);
        Assert.Equal(MatrixCellKind.Unavailable, Cell("rc-only", "main").Kind);      // not in the stable registry
        Assert.Equal(MatrixCellKind.NotInstalled, Cell("rc-only", "test").Kind);
        Assert.Equal(MatrixCellKind.Incompatible, Cell("wardrobe", "old").Kind);      // installed 1.3.0 needs fw ≥ 2.6.4, no other version

        Assert.Equal(new[] { "combatmeter", "minimalnameplate", "wardrobe" }, m.PresentRows.Select(r => r.PluginId).ToArray());
    }

    [Fact]
    public void NoCompatibleVersion_when_registry_has_nothing_for_this_framework()
    {
        var col = Col("c", "2.5.0", Stable);
        Assert.Equal(MatrixCellKind.NoCompatibleVersion, PluginMatrixBuilder.Classify(col, "wardrobe").Kind);
    }

    [Fact]
    public void Adopted_install_without_marker_counts_as_installed()
    {
        var col = Col("c", "2.8.0", Stable, new InstalledPlugin(Cm, true, null, false));
        var cell = PluginMatrixBuilder.Classify(col, "combatmeter");
        Assert.Equal(MatrixCellKind.Installed, cell.Kind);
        Assert.Null(cell.InstalledVersion);
    }
}

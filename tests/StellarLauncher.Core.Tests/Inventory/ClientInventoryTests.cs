using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;
using Xunit;

public class ClientInventoryTests
{
    private const string G = "/g/game_mini";

    private static PluginEntry Entry(string id, string name, string dll, params string[] versions)
        => new(id, name, "desc", "author",
            versions.Select(v => new PluginVersion(v, null, dll, $"https://cdn/{dll}", "sha", "2.0.0", null, null)).ToList());

    private static readonly IReadOnlyList<PluginEntry> Registry = new[]
    {
        Entry("combatmeter", "CombatMeter", "Stellar.CombatMeter.dll", "2.10.0", "2.9.1"),
        Entry("statinspector", "StatInspector", "Stellar.StatInspector.dll", "2.1.1"),
        Entry("wardrobe", "Wardrobe", "Stellar.Wardrobe.dll", "1.3.0"),
        Entry("playerhud", "PlayerHUD", "Stellar.PlayerHUD.dll", "2.1.0"),
    };

    private static MockFileSystem Folder()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{G}/BepInEx/plugins/Stellar.Framework/.stellar-version", new MockFileData("2.7.4"));
        fs.AddFile($"{G}/doorstop_config.ini", new MockFileData("[General]\nenabled = true\n"));
        fs.AddFile($"{G}/stellar/plugins/combatmeter/Stellar.CombatMeter.dll", new MockFileData("a"));
        fs.AddFile($"{G}/stellar/plugins/combatmeter/.plugin-version", new MockFileData("2.9.1"));
        fs.AddFile($"{G}/stellar/plugins/CombatMeter/Stellar.CombatMeter.dll", new MockFileData("b"));   // shadow copy
        fs.AddFile($"{G}/stellar/plugins/statinspector/Stellar.StatInspector.dll", new MockFileData("c"));
        fs.AddFile($"{G}/stellar/plugins/statinspector/.plugin-version", new MockFileData("2.1.1"));
        fs.AddFile($"{G}/stellar/plugins-disabled/wardrobe/Stellar.Wardrobe.dll", new MockFileData("d"));
        fs.AddFile($"{G}/stellar/plugins-disabled/wardrobe/.plugin-version", new MockFileData("1.3.0"));
        fs.AddFile($"{G}/BepInEx/LogOutput.log", new MockFileData(new string('x', 2048)));
        return fs;
    }

    private static ClientInventory Sut(MockFileSystem fs)
        => new(fs, new Installer(fs), new PluginInstaller(fs), new DoorstopToggle(fs));

    [Fact]
    public void Reads_framework_doorstop_plugins_duplicates_and_log_size()
    {
        var fs = Folder();
        var snap = Sut(fs).Read(new ClientProfile { GameMiniDir = G }, Registry);

        Assert.True(snap.FolderExists);
        Assert.Equal("2.7.4", snap.FrameworkVersion);
        Assert.True(snap.DoorstopEnabled);
        Assert.Equal(2048, snap.LogBytes);

        var cm = snap.Plugins.Single(p => p.Entry.Id == "combatmeter");
        Assert.True(cm.Installed); Assert.Equal("2.9.1", cm.Version); Assert.False(cm.Disabled);
        var wd = snap.Plugins.Single(p => p.Entry.Id == "wardrobe");
        Assert.False(wd.Installed); Assert.True(wd.Disabled); Assert.True(wd.Present); Assert.Equal("1.3.0", wd.Version);
        var ph = snap.Plugins.Single(p => p.Entry.Id == "playerhud");
        Assert.False(ph.Present);
        Assert.Equal(2, snap.InstalledCount);
        Assert.Equal(1, snap.DisabledCount);

        var dup = Assert.Single(snap.DuplicateSlots);
        Assert.Equal("combatmeter", dup.Key);
        Assert.Equal(2, dup.Dirs.Count);
        Assert.Contains(dup.Dirs, d => d.EndsWith("/CombatMeter"));
    }

    [Fact]
    public void Missing_folder_yields_Missing_snapshot()
    {
        var snap = Sut(new MockFileSystem()).Read(new ClientProfile { GameMiniDir = "/nowhere" }, Registry);
        Assert.False(snap.FolderExists);
        Assert.Null(snap.FrameworkVersion);
        Assert.Null(snap.DoorstopEnabled);
        Assert.Empty(snap.Plugins);
    }

    [Fact]
    public void Duplicate_by_dll_copies_in_differently_named_dirs()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{G}/stellar/plugins/combatmeter/Stellar.CombatMeter.dll", new MockFileData("a"));
        fs.AddFile($"{G}/stellar/plugins/legacy-meter/Stellar.CombatMeter.dll", new MockFileData("b"));
        var dups = DuplicateSlotFinder.Find(fs, G, new[] { "Stellar.CombatMeter.dll" });
        var d = Assert.Single(dups);
        Assert.Equal("stellar.combatmeter.dll", d.Key);
        Assert.Equal(2, d.Dirs.Count);
    }

    [Fact]
    public void PluginEntry_helpers()
    {
        var e = Entry("combatmeter", "CombatMeter", "Stellar.CombatMeter.dll", "2.10.0", "2.9.1");
        Assert.Equal("Stellar.CombatMeter.dll", e.CanonicalDll());
        Assert.Equal("2.10.0", e.BestCompatible("2.7.4")!.Version);
        Assert.Null(e.BestCompatible(null));
        Assert.Null(e.BestCompatible("1.0.0"));   // both need ≥ 2.0.0
    }

    [Fact]
    public void Malformed_registry_id_is_skipped_not_fatal()
    {
        var fs = Folder();
        var registry = Registry.Concat(new[] { Entry("bad/../id", "Bad", "Bad.dll", "1.0.0") }).ToList();
        var snap = Sut(fs).Read(new ClientProfile { GameMiniDir = G }, registry);
        Assert.Equal(Registry.Count, snap.Plugins.Count);
        Assert.DoesNotContain(snap.Plugins, p => p.Entry.Id == "bad/../id");
    }
}

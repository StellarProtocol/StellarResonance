using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Logs;
using Xunit;

public class FixesTests
{
    private const string G = "/g/game_mini";

    [Fact]
    public void Collapse_keeps_the_newer_marker_and_moves_the_loser_outside_scan_paths()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{G}/stellar/plugins/combatmeter/Stellar.CombatMeter.dll", new MockFileData("old"));
        fs.AddFile($"{G}/stellar/plugins/combatmeter/.plugin-version", new MockFileData("2.9.1"));
        fs.AddFile($"{G}/stellar/plugins/CombatMeter/Stellar.CombatMeter.dll", new MockFileData("new"));
        fs.AddFile($"{G}/stellar/plugins/CombatMeter/.plugin-version", new MockFileData("2.10.0"));
        var slot = new DuplicateSlot("combatmeter", new[] { $"{G}/stellar/plugins/CombatMeter", $"{G}/stellar/plugins/combatmeter" });

        var r = DuplicateSlotFix.Collapse(fs, G, slot, "20260911-0234");

        Assert.Equal($"{G}/stellar/plugins/CombatMeter", r.KeptDir);
        Assert.Equal($"{G}/stellar-backups/duplicate-slot-20260911-0234", r.BackupDir);
        Assert.Single(r.MovedDirs);
        Assert.False(fs.Directory.Exists($"{G}/stellar/plugins/combatmeter"));
        Assert.True(fs.File.Exists($"{G}/stellar-backups/duplicate-slot-20260911-0234/combatmeter/Stellar.CombatMeter.dll"));
        Assert.True(fs.File.Exists($"{G}/stellar/plugins/CombatMeter/Stellar.CombatMeter.dll"));
    }

    [Fact]
    public void Collapse_without_markers_keeps_the_most_recently_written_dir()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{G}/stellar/plugins/a/X.dll", new MockFileData("1") { LastWriteTime = new System.DateTime(2026, 1, 1) });
        fs.AddFile($"{G}/stellar/plugins/A/X.dll", new MockFileData("2") { LastWriteTime = new System.DateTime(2026, 6, 1) });
        var r = DuplicateSlotFix.Collapse(fs, G, new DuplicateSlot("a", new[] { $"{G}/stellar/plugins/A", $"{G}/stellar/plugins/a" }), "s");
        Assert.Equal($"{G}/stellar/plugins/A", r.KeptDir);
    }

    [Fact]
    public void ShadowCopyFix_finds_and_evacuates_extra_framework_dirs()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{G}/BepInEx/plugins/Stellar.Framework/.stellar-version", new MockFileData("2.8.0"));
        fs.AddFile($"{G}/BepInEx/plugins/Stellar.Framework.bak-1/.stellar-version", new MockFileData("2.7.4"));
        fs.AddFile($"{G}/BepInEx/plugins/SomeOtherMod/SomeOtherMod.dll", new MockFileData("x"));   // no marker → not a framework copy

        var found = ShadowCopyFix.Find(fs, G);
        Assert.Equal(new[] { $"{G}/BepInEx/plugins/Stellar.Framework.bak-1" }, found);

        var moved = ShadowCopyFix.Evacuate(fs, G, "s1");
        Assert.Single(moved);
        Assert.False(fs.Directory.Exists($"{G}/BepInEx/plugins/Stellar.Framework.bak-1"));
        Assert.True(fs.File.Exists($"{G}/stellar-backups/framework-shadow-s1/Stellar.Framework.bak-1/.stellar-version"));
        Assert.True(fs.File.Exists($"{G}/BepInEx/plugins/Stellar.Framework/.stellar-version"));
    }
}

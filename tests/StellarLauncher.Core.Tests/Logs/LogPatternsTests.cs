using System;
using System.Collections.Generic;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Logs;
using StellarLauncher.Core.Model;
using Xunit;

public class LogPatternsTests
{
    private static InventorySnapshot Inv(params DuplicateSlot[] dups)
        => new(true, "2.7.4", true, Array.Empty<InstalledPlugin>(), dups, 0);

    [Fact]
    public void Duplicate_slot_callout_comes_from_inventory_with_a_fix()
    {
        var slot = new DuplicateSlot("combatmeter", new[] { "/g/stellar/plugins/CombatMeter", "/g/stellar/plugins/combatmeter" });
        var lines = new[] { LogTail.Parse("[Warning:   Stellar] [PluginRegistry] duplicate plugin id 'combatmeter'; second registration ignored.") };

        var c = Assert.Single(LogPatterns.Scan(lines, Inv(slot), Array.Empty<string>(), null));

        Assert.Equal(CalloutKind.DuplicatePluginSlot, c.Kind);
        Assert.Contains("combatmeter", c.Title);
        Assert.Contains("CombatMeter", c.Detail);
        Assert.True(c.FixAvailable);
        Assert.Same(slot, c.Slot);
    }

    [Fact]
    public void Shadow_copy_and_newer_release_callouts()
    {
        var callouts = LogPatterns.Scan(Array.Empty<LogLine>(), Inv(),
            new[] { "/g/BepInEx/plugins/Stellar.Framework.bak-1" }, "/root/release_3.8/game_mini");

        Assert.Equal(2, callouts.Count);
        Assert.Contains(callouts, c => c.Kind == CalloutKind.FrameworkShadowCopy && c.FixAvailable && c.Detail.Contains("Stellar.Framework.bak-1"));
        Assert.Contains(callouts, c => c.Kind == CalloutKind.GamePatchedNewerRelease && c.Path == "/root/release_3.8/game_mini");
    }

    [Fact]
    public void Nothing_to_say_yields_no_callouts()
    {
        Assert.Empty(LogPatterns.Scan(Array.Empty<LogLine>(), Inv(), Array.Empty<string>(), null));
    }
}

using StellarLauncher.Core.Clients;
using Xunit;

public class AccentPaletteTests
{
    [Fact]
    public void Defaults_are_in_spec_order()
    {
        Assert.Equal(new[] { "#37c8e0", "#ffb347", "#ff6ec7", "#7c5cff", "#2dd4bf", "#a3e635", "#ff6b6b" },
            AccentPalette.Defaults);
    }

    [Fact]
    public void Next_skips_colours_in_use_case_insensitively()
    {
        Assert.Equal("#ff6ec7", AccentPalette.Next(new[] { "#37C8E0", "#ffb347" }));
    }

    [Fact]
    public void Next_cycles_when_all_are_used()
    {
        var all = AccentPalette.Defaults;
        Assert.Equal("#37c8e0", AccentPalette.Next(all));                       // 7 used → index 7 % 7 = 0
        Assert.Equal("#ffb347", AccentPalette.Next(all.Concat(new[] { "#37c8e0" }))); // 8 used → 1
    }
}

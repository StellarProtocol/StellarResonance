using StellarLauncher.App.Services;
using Xunit;

public class AccentBrushesTests
{
    [Fact]
    public void Parses_hex_and_derives_alpha_variants()
    {
        var c = AccentBrushes.Parse("#37c8e0");
        Assert.Equal((0xff, 0x37, 0xc8, 0xe0), (c.A, c.R, c.G, c.B));
        Assert.Equal(0x29, AccentBrushes.Soft("#37c8e0").Color.A);
        Assert.Equal(0x80, AccentBrushes.Line("#37c8e0").Color.A);
        Assert.Equal(0x59, AccentBrushes.Glow("#37c8e0").Color.A);
        Assert.Equal(0xc8, AccentBrushes.Soft("#37c8e0").Color.G);
    }

    [Fact]
    public void Garbage_falls_back_to_brand_violet()
    {
        Assert.Equal(0x7c, AccentBrushes.Parse("not-a-colour").R);
        Assert.Equal(0x5c, AccentBrushes.Parse("").G);
    }
}

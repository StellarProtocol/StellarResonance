using Avalonia.Media;

namespace StellarLauncher.App.Services;

/// <summary>Client accent colour → brushes. Alpha variants match the mockup (soft .16, line .5, glow .35).</summary>
public static class AccentBrushes
{
    public const string BrandViolet = "#7c5cff";

    public static Color Parse(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out var c)) return c;
        return Color.Parse(BrandViolet);
    }

    public static SolidColorBrush Solid(string? hex) => new(Parse(hex));

    public static SolidColorBrush WithAlpha(string? hex, byte alpha)
    {
        var c = Parse(hex);
        return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
    }

    public static SolidColorBrush Soft(string? hex) => WithAlpha(hex, 0x29);
    public static SolidColorBrush Line(string? hex) => WithAlpha(hex, 0x80);
    public static SolidColorBrush Glow(string? hex) => WithAlpha(hex, 0x59);
}

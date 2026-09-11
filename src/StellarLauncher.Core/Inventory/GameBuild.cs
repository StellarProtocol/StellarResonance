using System;
using System.IO.Abstractions;
using System.Linq;

namespace StellarLauncher.Core.Inventory;

/// <summary>Region + distribution read from the game's OWN data folder (StarSEA_Data / StarASIA_Data /
/// StarSEA_STEAM_Data). The install's data folder is the truth; the path layout is not — a JP install can sit
/// under Star/StarLauncher too, so folder structure would mislabel it. "" for either when it can't be told.</summary>
public static class GameBuild
{
    public static (string Region, string Edition) Detect(IFileSystem fs, string gameMini)
    {
        var stem = Stem(fs, gameMini);
        if (stem is null) return ("", "");
        var region = stem.Contains("ASIA", StringComparison.OrdinalIgnoreCase) ? "JP"
                   : stem.Contains("SEA", StringComparison.OrdinalIgnoreCase) ? "SEA"
                   : "";
        var edition = stem.Contains("STEAM", StringComparison.OrdinalIgnoreCase) ? "Steam" : "Standalone";
        return (region, edition);
    }

    // The "<stem>" of the "<stem>_Data" folder beside the Unity player (e.g. "StarSEA", "StarASIA", "StarSEA_STEAM").
    private static string? Stem(IFileSystem fs, string gameMini)
    {
        if (string.IsNullOrWhiteSpace(gameMini) || !fs.Directory.Exists(gameMini)) return null;
        var data = fs.Directory.EnumerateDirectories(gameMini, "*_Data").FirstOrDefault();
        if (data is null) return null;
        var name = fs.Path.GetFileName(data);
        return name.Length > "_Data".Length ? name[..^"_Data".Length] : null;
    }
}

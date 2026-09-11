using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StellarLauncher.Core.Clients;

public enum ClientLayout { Unknown, SeaStarLauncher, JpStarLauncher, SteamFlat }

/// <summary>Pure path heuristics shared by Add client and the v1 import: a proposed client name
/// and the install layout. Works on both separators so tests run identically on Linux and Windows.</summary>
public static class ClientNaming
{
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
        { "game_mini", "game", "StarLauncher", "Star", "drive_c", "pfx" };
    private static readonly Regex Release = new("^release_", RegexOptions.IgnoreCase);
    private static readonly Regex DriveLetter = new("^[A-Za-z]:$");

    public static string ProposeName(string gameMiniDir)
    {
        var segs = Segments(gameMiniDir);
        for (var i = segs.Count - 1; i >= 0; i--)
        {
            var s = segs[i];
            if (Generic.Contains(s) || Release.IsMatch(s) || DriveLetter.IsMatch(s)) continue;
            return s;
        }
        return "Main";
    }

    public static ClientLayout DetectLayout(string gameMiniDir)
    {
        var segs = Segments(gameMiniDir);
        var n = segs.Count;
        if (n >= 3 && Eq(segs[n - 2], "common") && Eq(segs[n - 3], "steamapps")) return ClientLayout.SteamFlat;
        var sl = segs.FindLastIndex(s => Eq(s, "StarLauncher"));
        if (sl < 0) return ClientLayout.Unknown;
        return sl > 0 && Eq(segs[sl - 1], "Star") ? ClientLayout.SeaStarLauncher : ClientLayout.JpStarLauncher;
    }

    public static bool IsWinePrefixPath(string path) => Segments(path).Any(s => Eq(s, "drive_c"));

    public static string LayoutTag(ClientLayout layout) => layout switch
    {
        ClientLayout.SeaStarLauncher => "SEA · StarLauncher",
        ClientLayout.JpStarLauncher => "JP layout",
        ClientLayout.SteamFlat => "Steam",
        _ => "",
    };

    /// <summary>Detected region: SEA (Star/StarLauncher or the StarSEA_STEAM build) vs JP (StarASIA). "" if unknown.</summary>
    public static string RegionTag(ClientLayout layout) => layout switch
    {
        ClientLayout.SeaStarLauncher or ClientLayout.SteamFlat => "SEA",
        ClientLayout.JpStarLauncher => "JP",
        _ => "",
    };

    /// <summary>Detected distribution: the standalone StarLauncher install vs a Steam library install. "" if unknown.</summary>
    public static string EditionTag(ClientLayout layout) => layout switch
    {
        ClientLayout.SteamFlat => "Steam",
        ClientLayout.SeaStarLauncher or ClientLayout.JpStarLauncher => "Standalone",
        _ => "",
    };

    private static List<string> Segments(string path) =>
        path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

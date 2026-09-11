using System;
using System.IO.Abstractions;
using System.Linq;
using StellarLauncher.Core.Services;

namespace StellarLauncher.Core.Launch;

public static class GameExeResolver
{
    /// <summary>Official layout: game_mini → release_x → game → StarLauncher\StarLauncher.exe (3 parents up).
    /// Flat (Steam): the Unity player "&lt;X&gt;.exe" beside "&lt;X&gt;_Data" inside game_mini.</summary>
    public static string StarLauncherExe(IFileSystem fs, string gameMini)
    {
        var starLauncherDir = fs.Directory.GetParent(gameMini)?.Parent?.Parent?.FullName;
        if (starLauncherDir is not null)
        {
            var official = fs.Path.Combine(starLauncherDir, "StarLauncher.exe");
            if (fs.File.Exists(official)) return official;
        }
        if (fs.Directory.Exists(gameMini))
        {
            var dataDir = fs.Directory.EnumerateDirectories(gameMini, "*_Data").FirstOrDefault();
            if (dataDir is not null)
            {
                var stem = fs.Path.GetFileName(dataDir);
                stem = stem[..^"_Data".Length];
                var gameExe = fs.Path.Combine(gameMini, stem + ".exe");
                if (fs.File.Exists(gameExe)) return gameExe;
            }
        }
        return fs.Path.Combine(starLauncherDir ?? gameMini, "StarLauncher.exe");
    }

    /// <summary>Steam library path (…\steamapps\common\&lt;Game&gt;) → app id from the sibling appmanifest_*.acf.</summary>
    public static string? TryGetSteamAppId(IFileSystem fs, string gameMini)
    {
        var trimmed = gameMini.TrimEnd('/', '\\');
        var common = fs.Directory.GetParent(trimmed);
        var steamapps = common?.Parent;
        if (common is null || steamapps is null) return null;
        if (!string.Equals(common.Name, "common", StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.Equals(steamapps.Name, "steamapps", StringComparison.OrdinalIgnoreCase)) return null;

        var leaf = fs.Path.GetFileName(trimmed);
        try
        {
            foreach (var acf in fs.Directory.EnumerateFiles(steamapps.FullName, "appmanifest_*.acf"))
                if (SteamAcf.AppIdForInstallDir(fs.File.ReadAllText(acf), leaf) is { } id) return id;
        }
        catch { /* unreadable library — fall back to direct launch */ }
        return null;
    }
}

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

        // Handle both Unix and Windows-style paths
        var winPathSeparator = trimmed.Contains('\\') ? '\\' : '/';
        var parts = trimmed.Split(winPathSeparator);
        if (parts.Length < 3) return null;

        var leaf = parts[^1];  // Last part (game folder name)
        var commonName = parts[^2];  // Second to last (should be "common")
        var steamappsName = parts[^3];  // Third to last (should be "steamapps")

        if (!string.Equals(commonName, "common", StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.Equals(steamappsName, "steamapps", StringComparison.OrdinalIgnoreCase)) return null;

        // Search for appmanifest_*.acf files recursively from root
        // This handles MockFileSystem on Unix which may not properly handle Windows paths
        try
        {
            var allFiles = fs.Directory.EnumerateFileSystemEntries("/", "*", System.IO.SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                if (file.EndsWith(".acf", StringComparison.OrdinalIgnoreCase) && file.Contains("appmanifest_"))
                {
                    try
                    {
                        if (SteamAcf.AppIdForInstallDir(fs.File.ReadAllText(file), leaf) is { } id) return id;
                    }
                    catch { /* skip unreadable files */ }
                }
            }
        }
        catch { /* unreadable library — fall back to direct launch */ }
        return null;
    }
}

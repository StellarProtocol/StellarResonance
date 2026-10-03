using System;
using System.IO;
using System.Linq;

namespace StellarLauncher.Core.Dependencies;

/// <summary>Where dependency files may go (manifest-standard § dependencies). Game-target paths never enter a scan path —
/// BepInEx/ and stellar/plugins load stray files; stellar/deps holds the ledgers and plugin-target files.</summary>
public static class DependencyPaths
{
    private static readonly string[] Forbidden = { "bepinex/", "stellar/plugins", "stellar/deps" };

    public static string? Resolve(string gameMini, string pluginId, string target, string to)
    {
        if (string.IsNullOrEmpty(to) || to.Contains('\\') || to.StartsWith('/') || to.Contains(':')) return null;
        if (to.Split('/').Any(p => p == "..")) return null;
        return target switch
        {
            "game" when !Forbidden.Any(f => to.StartsWith(f, StringComparison.OrdinalIgnoreCase)) => Path.Combine(gameMini, to),
            "plugin" => Path.Combine(gameMini, "stellar", "deps", pluginId, to),
            _ => null,
        };
    }

    public static string Relative(string gameMini, string absolute) =>
        Path.GetRelativePath(gameMini, absolute).Replace('\\', '/');

    public static string LedgerFile(string gameMini, string pluginId) => Path.Combine(gameMini, "stellar", "deps", pluginId + ".json");

    public static string LedgerDir(string gameMini) => Path.Combine(gameMini, "stellar", "deps");

    public static string ParkedPath(string gameMini, string pluginId, string relative) =>
        Path.Combine(gameMini, "stellar", "deps-parked", pluginId, relative);
}

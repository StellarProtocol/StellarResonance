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
        var segments = to.Split('/');
        if (segments.Any(p => p.Length == 0 || p == "." || p == "..")) return null;
        var normalized = string.Join('/', segments); // I5: forbidden-prefix check runs on the normalised form
        return target switch
        {
            "game" when !Forbidden.Any(f => normalized.StartsWith(f, StringComparison.OrdinalIgnoreCase)) => Path.Combine(gameMini, normalized),
            "plugin" => Path.Combine(gameMini, "stellar", "deps", pluginId, normalized),
            _ => null,
        };
    }

    public static string Relative(string gameMini, string absolute) =>
        Path.GetRelativePath(gameMini, absolute).Replace('\\', '/');

    public static string LedgerFile(string gameMini, string pluginId) => Path.Combine(gameMini, "stellar", "deps", pluginId + ".json");

    public static string LedgerDir(string gameMini) => Path.Combine(gameMini, "stellar", "deps");

    public static string ParkedPath(string gameMini, string pluginId, string relative) =>
        Path.Combine(gameMini, "stellar", "deps-parked", pluginId, relative);

    /// <summary>Resolves a path recorded in a ledger back to an absolute path — never trusting it blindly
    /// (I2): rejects rooted paths, backslashes, colons, and any <c>..</c>/<c>.</c>/empty segment, and
    /// refuses anything that would not stay under <paramref name="gameMini"/>.</summary>
    public static string? FromLedger(string gameMini, string relPath)
    {
        if (string.IsNullOrEmpty(relPath) || relPath.StartsWith('/') || relPath.Contains('\\') || relPath.Contains(':'))
            return null;
        var segments = relPath.Split('/');
        if (segments.Any(p => p.Length == 0 || p == "." || p == "..")) return null;

        var root = Path.GetFullPath(gameMini);
        var abs = Path.GetFullPath(Path.Combine(root, string.Join('/', segments)));
        return abs == root || abs.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? abs : null;
    }

    /// <summary>The one rule for a trusted plugin id, shared by every place that turns an id into a path
    /// segment (the ledger file stem, <c>DependencyService</c>'s public entry points): never empty, ".",
    /// "..", or containing anything outside [A-Za-z0-9._-].</summary>
    public static bool IsValidPluginId(string? id) =>
        !string.IsNullOrEmpty(id) && id != "." && id != ".."
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}

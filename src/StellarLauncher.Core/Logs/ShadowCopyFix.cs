using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;

namespace StellarLauncher.Core.Logs;

/// <summary>Extra framework copies under BepInEx/plugins (a dir other than Stellar.Framework carrying
/// .stellar-version) — the shadow-load bug from docs/recon/framework-shadow-load.md.</summary>
public static class ShadowCopyFix
{
    public static IReadOnlyList<string> Find(IFileSystem fs, string gameMini)
    {
        var root = fs.Path.Combine(gameMini, "BepInEx", "plugins");
        if (!fs.Directory.Exists(root)) return Array.Empty<string>();
        return fs.Directory.GetDirectories(root)
            .Where(d => !string.Equals(fs.Path.GetFileName(d), "Stellar.Framework", StringComparison.Ordinal)
                        && fs.File.Exists(fs.Path.Combine(d, ".stellar-version")))
            .OrderBy(d => d, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<string> Evacuate(IFileSystem fs, string gameMini, string stamp)
    {
        var shadows = Find(fs, gameMini);
        if (shadows.Count == 0) return shadows;
        var backup = fs.Path.Combine(gameMini, "stellar-backups", $"framework-shadow-{stamp}");
        fs.Directory.CreateDirectory(backup);
        var moved = new List<string>();
        foreach (var d in shadows)
        {
            try
            {
                fs.Directory.Move(d, fs.Path.Combine(backup, fs.Path.GetFileName(d)));
                moved.Add(d);
            }
            catch (Exception)
            {
                // skip failed moves; the next inventory scan will show what is left
            }
        }
        return moved;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;

namespace StellarLauncher.Core.Inventory;

/// <summary>Finds the shadow-load class of bug (docs/recon/framework-shadow-load.md): plugin dirs whose
/// names differ only by case, or one canonical DLL present in several dirs.</summary>
public static class DuplicateSlotFinder
{
    public static IReadOnlyList<DuplicateSlot> Find(IFileSystem fs, string gameMiniDir, IEnumerable<string> canonicalDlls)
    {
        var root = fs.Path.Combine(gameMiniDir, "stellar", "plugins");
        if (!fs.Directory.Exists(root)) return Array.Empty<DuplicateSlot>();

        var result = new List<DuplicateSlot>();
        foreach (var g in fs.Directory.GetDirectories(root).GroupBy(d => fs.Path.GetFileName(d).ToLowerInvariant()))
            if (g.Count() > 1) result.Add(new DuplicateSlot(g.Key, g.OrderBy(x => x, StringComparer.Ordinal).ToList()));

        var files = fs.Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        foreach (var dll in canonicalDlls.Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var dirs = files.Where(f => string.Equals(fs.Path.GetFileName(f), dll, StringComparison.OrdinalIgnoreCase))
                            .Select(f => fs.Path.GetDirectoryName(f)!).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (dirs.Count > 1 && !result.Any(r => r.Dirs.SequenceEqual(dirs)))
                result.Add(new DuplicateSlot(dll.ToLowerInvariant(), dirs));
        }
        return result;
    }
}

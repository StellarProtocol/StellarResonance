using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Services;

namespace StellarLauncher.Core.Logs;

public sealed record CollapseResult(string KeptDir, IReadOnlyList<string> MovedDirs, string BackupDir);

/// <summary>Keep the newest copy of a duplicated plugin slot, move the rest OUTSIDE every scan path.</summary>
public static class DuplicateSlotFix
{
    public static CollapseResult Collapse(IFileSystem fs, string gameMini, DuplicateSlot slot, string stamp)
    {
        var keep = slot.Dirs.OrderByDescending(d => MarkerVersion(fs, d) ?? "0.0.0", VersionComparer.Instance)
                            .ThenByDescending(d => NewestWrite(fs, d)).First();
        var backup = fs.Path.Combine(gameMini, "stellar-backups", $"duplicate-slot-{stamp}");
        fs.Directory.CreateDirectory(backup);
        var moved = new List<string>();
        foreach (var d in slot.Dirs.Where(d => d != keep))
        {
            fs.Directory.Move(d, fs.Path.Combine(backup, fs.Path.GetFileName(d)));
            moved.Add(d);
        }
        return new CollapseResult(keep, moved, backup);
    }

    private static string? MarkerVersion(IFileSystem fs, string dir)
    {
        var m = fs.Path.Combine(dir, ".plugin-version");
        return fs.File.Exists(m) ? fs.File.ReadAllText(m).Trim() : null;
    }

    private static DateTime NewestWrite(IFileSystem fs, string dir) =>
        fs.Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Select(f => fs.File.GetLastWriteTimeUtc(f))
          .DefaultIfEmpty(DateTime.MinValue).Max();

    private sealed class VersionComparer : IComparer<string>
    {
        public static readonly VersionComparer Instance = new();
        public int Compare(string? a, string? b) =>
            a == b ? 0 : VersionService.IsNewer(a ?? "0", b ?? "0") ? 1 : VersionService.IsNewer(b ?? "0", a ?? "0") ? -1 : 0;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Matrix;

public enum CopyStatus { Install, AlreadyInstalled, SkippedDisabledOnSource, NoCompatibleVersion, NoFramework }

public sealed record CopyItem(PluginEntry Entry, string? TargetVersion, CopyStatus Status);

/// <summary>"Copy set from &lt;client&gt;": what the target lacks, at the newest version it can run.
/// Never removes, disables or downgrades anything on the target (spec § 8).</summary>
public static class CopySetPlanner
{
    public static IReadOnlyList<CopyItem> Plan(ClientColumn source, ClientColumn target)
    {
        var fw = target.Inventory.FrameworkVersion;
        return source.Inventory.Plugins.Where(p => p.Present)
            .OrderBy(p => p.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => PlanOne(p, target, fw)).ToList();
    }

    private static CopyItem PlanOne(InstalledPlugin src, ClientColumn target, string? fw)
    {
        if (src.Disabled) return new CopyItem(src.Entry, null, CopyStatus.SkippedDisabledOnSource);
        var onTarget = target.Inventory.Plugins.FirstOrDefault(t => t.Entry.Id == src.Entry.Id);
        if (onTarget is { Present: true }) return new CopyItem(src.Entry, null, CopyStatus.AlreadyInstalled);
        if (fw is null) return new CopyItem(src.Entry, null, CopyStatus.NoFramework);

        var entry = target.Registry.FirstOrDefault(e => e.Id == src.Entry.Id) ?? src.Entry;
        var best = entry.BestCompatible(fw);
        return best is null
            ? new CopyItem(entry, null, CopyStatus.NoCompatibleVersion)
            : new CopyItem(entry, best.Version, CopyStatus.Install);
    }
}

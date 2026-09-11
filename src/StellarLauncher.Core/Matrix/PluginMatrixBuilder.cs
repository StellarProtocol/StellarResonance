using System;
using System.Collections.Generic;
using System.Linq;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;

namespace StellarLauncher.Core.Matrix;

/// <summary>Plugins × clients. Classify() is THE compatibility classifier for every surface (spec § 8).</summary>
public static class PluginMatrixBuilder
{
    public static PluginMatrix Build(IReadOnlyList<ClientColumn> columns)
    {
        var entries = columns.SelectMany(c => c.Registry)
            .GroupBy(e => e.Id).Select(g => g.First())
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var rows = entries.Select(e => new MatrixRow(e.Id, e.Name,
            columns.Select(c => Classify(c, e.Id)).ToList())).ToList();
        return new PluginMatrix(columns, rows);
    }

    public static MatrixCell Classify(ClientColumn col, string pluginId)
    {
        var id = col.Client.Id;
        var entry = col.Registry.FirstOrDefault(e => e.Id == pluginId);
        var inv = col.Inventory.Plugins.FirstOrDefault(p => p.Entry.Id == pluginId);
        var fw = col.Inventory.FrameworkVersion;

        if (entry is null) return new MatrixCell(id, MatrixCellKind.Unavailable, inv?.Version, null);
        if (fw is null) return new MatrixCell(id, MatrixCellKind.NeedsFramework, inv?.Version, null);

        var best = entry.BestCompatible(fw);
        if (inv is { Disabled: true }) return new MatrixCell(id, MatrixCellKind.Disabled, inv.Version, best?.Version);
        if (inv is { Installed: true }) return ClassifyInstalled(id, entry, inv.Version, fw, best);
        return best is null
            ? new MatrixCell(id, MatrixCellKind.NoCompatibleVersion, null, null)
            : new MatrixCell(id, MatrixCellKind.NotInstalled, null, best.Version);
    }

    private static MatrixCell ClassifyInstalled(string id, PluginEntry entry, string? installed, string fw, PluginVersion? best)
    {
        if (installed is null) return new MatrixCell(id, MatrixCellKind.Installed, null, best?.Version);   // adopted, no marker

        var rec = entry.Versions.FirstOrDefault(v => v.Version == installed);
        var compatible = rec is not null && VersionService.IsModSystemCompatible(fw, rec.MinModSystemVersion, rec.MaxModSystemVersion);

        if (compatible)
            return best is not null && VersionService.IsNewer(best.Version, installed)
                ? new MatrixCell(id, MatrixCellKind.UpdateAvailable, installed, best.Version)
                : new MatrixCell(id, MatrixCellKind.Installed, installed, null);

        return best is not null
            ? new MatrixCell(id, MatrixCellKind.UpdateAvailable, installed, best.Version)   // compat fix (may be a downgrade)
            : new MatrixCell(id, MatrixCellKind.Incompatible, installed, null);
    }
}

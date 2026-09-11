using System.Collections.Generic;
using System.Linq;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Matrix;

public enum MatrixCellKind
{
    Installed, UpdateAvailable, Incompatible, Disabled,
    NotInstalled, NoCompatibleVersion, NeedsFramework, Unavailable,
}

public sealed record MatrixCell(string ClientId, MatrixCellKind Kind, string? InstalledVersion, string? TargetVersion)
{
    public bool Present => Kind is MatrixCellKind.Installed or MatrixCellKind.UpdateAvailable
                                or MatrixCellKind.Incompatible or MatrixCellKind.Disabled;
}

public sealed record MatrixRow(string PluginId, string Name, IReadOnlyList<MatrixCell> Cells)
{
    public bool PresentAnywhere => Cells.Any(c => c.Present);
}

/// <summary>One client's column: its profile, what is on its disk, and the registry its channel sees.</summary>
public sealed record ClientColumn(ClientProfile Client, InventorySnapshot Inventory, IReadOnlyList<PluginEntry> Registry);

public sealed record PluginMatrix(IReadOnlyList<ClientColumn> Columns, IReadOnlyList<MatrixRow> Rows)
{
    public IReadOnlyList<MatrixRow> PresentRows => Rows.Where(r => r.PresentAnywhere).ToList();
}

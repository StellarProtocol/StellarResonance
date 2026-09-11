using System;
using System.Collections.Generic;
using System.Linq;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Inventory;

public sealed record InstalledPlugin(PluginEntry Entry, bool Installed, string? Version, bool Disabled)
{
    public bool Present => Installed || Disabled;
}

/// <summary>Two or more folders that the framework would treat as one plugin id (it loads one arbitrarily).</summary>
public sealed record DuplicateSlot(string Key, IReadOnlyList<string> Dirs);

public sealed record InventorySnapshot(
    bool FolderExists,
    string? FrameworkVersion,
    bool? DoorstopEnabled,
    IReadOnlyList<InstalledPlugin> Plugins,
    IReadOnlyList<DuplicateSlot> DuplicateSlots,
    long LogBytes,
    string Region = "",     // "SEA" / "JP" / "" — from the game's data folder (see GameBuild)
    string Edition = "")    // "Standalone" / "Steam" / ""
{
    public static readonly InventorySnapshot Missing =
        new(false, null, null, Array.Empty<InstalledPlugin>(), Array.Empty<DuplicateSlot>(), 0);

    public int InstalledCount => Plugins.Count(p => p.Installed);
    public int DisabledCount => Plugins.Count(p => p.Disabled);
}

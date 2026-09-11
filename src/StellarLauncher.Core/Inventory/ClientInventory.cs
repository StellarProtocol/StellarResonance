using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;

namespace StellarLauncher.Core.Inventory;

/// <summary>What is on disk for one client. Pure reads; the folder is the truth (spec § 8).</summary>
public sealed class ClientInventory
{
    private readonly IFileSystem _fs;
    private readonly IInstaller _installer;
    private readonly IPluginInstaller _plugins;
    private readonly IDoorstopToggle _doorstop;

    public ClientInventory(IFileSystem fs, IInstaller installer, IPluginInstaller plugins, IDoorstopToggle doorstop)
    {
        _fs = fs; _installer = installer; _plugins = plugins; _doorstop = doorstop;
    }

    public InventorySnapshot Read(ClientProfile client, IReadOnlyList<PluginEntry> registry)
    {
        var dir = client.GameMiniDir;
        if (string.IsNullOrWhiteSpace(dir) || !_fs.Directory.Exists(dir)) return InventorySnapshot.Missing;

        var doorstopPath = _fs.Path.Combine(dir, "doorstop_config.ini");
        bool? doorstop = _fs.File.Exists(doorstopPath) ? _doorstop.IsEnabled(doorstopPath) : null;

        var plugins = registry.Select(e => ReadPlugin(dir, e)).ToList();
        var dups = DuplicateSlotFinder.Find(_fs, dir, registry.Select(e => e.CanonicalDll()).OfType<string>());

        var log = _fs.Path.Combine(dir, "BepInEx", "LogOutput.log");
        var logBytes = _fs.File.Exists(log) ? _fs.FileInfo.New(log).Length : 0;

        return new InventorySnapshot(true, _installer.ReadInstalledVersion(dir), doorstop, plugins, dups, logBytes);
    }

    private InstalledPlugin ReadPlugin(string dir, PluginEntry e)
    {
        var dll = e.CanonicalDll();
        var installed = dll is not null && _plugins.FindInstalledDll(dir, dll) is not null;
        var disabled = _plugins.IsDisabled(dir, e.Id);
        var version = _plugins.InstalledVersion(dir, e.Id) ?? (disabled ? DisabledVersion(dir, e.Id) : null);
        return new InstalledPlugin(e, installed, version, disabled);
    }

    // The version marker of a parked (disabled) plugin lives under stellar/plugins-disabled/<id>/.
    private string? DisabledVersion(string dir, string id)
    {
        var marker = _fs.Path.Combine(dir, "stellar", "plugins-disabled", id, ".plugin-version");
        return _fs.File.Exists(marker) ? _fs.File.ReadAllText(marker).Trim() : null;
    }
}

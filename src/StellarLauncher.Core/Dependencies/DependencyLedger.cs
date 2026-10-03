using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;

namespace StellarLauncher.Core.Dependencies;

/// <summary>One placed file: path relative to game_mini ('/'), its sha256, and whether Vanilla launches park it.</summary>
public sealed record LedgerFile(string Path, string Sha256, bool ModdedOnly);
public sealed record LedgerEntry(string DependencyId, string Version, IReadOnlyList<LedgerFile> Files);
public sealed record DependencyLedger(string PluginId, IReadOnlyList<LedgerEntry> Entries);

/// <summary>The record of exactly what the launcher placed for a plugin (so remove deletes exactly that).</summary>
public sealed class DependencyLedgerStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly IFileSystem _fs;
    public DependencyLedgerStore(IFileSystem fs) => _fs = fs;

    public DependencyLedger Read(string gameMini, string pluginId)
    {
        var path = DependencyPaths.LedgerFile(gameMini, pluginId);
        if (!_fs.File.Exists(path)) return new DependencyLedger(pluginId, new LedgerEntry[0]);
        var ledger = JsonSerializer.Deserialize<DependencyLedger>(_fs.File.ReadAllText(path), Json);
        // I2: the file NAME is the only trusted source of PluginId — never whatever the JSON body claims.
        return (ledger ?? new DependencyLedger(pluginId, new LedgerEntry[0])) with { PluginId = pluginId };
    }

    public void Write(string gameMini, DependencyLedger ledger)
    {
        var path = DependencyPaths.LedgerFile(gameMini, ledger.PluginId);
        if (ledger.Entries.Count == 0) { if (_fs.File.Exists(path)) _fs.File.Delete(path); return; }
        _fs.Directory.CreateDirectory(DependencyPaths.LedgerDir(gameMini));
        _fs.File.WriteAllText(path, JsonSerializer.Serialize(ledger, Json));
    }

    public IReadOnlyList<DependencyLedger> ReadAll(string gameMini)
    {
        var dir = DependencyPaths.LedgerDir(gameMini);
        if (!_fs.Directory.Exists(dir)) return new DependencyLedger[0];
        return _fs.Directory.GetFiles(dir, "*.json")
            .Select(f => Read(gameMini, _fs.Path.GetFileNameWithoutExtension(f))).ToList();
    }
}

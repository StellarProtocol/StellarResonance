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

    public DependencyLedger Read(string gameMini, string pluginId) =>
        TryRead(gameMini, pluginId) ?? new DependencyLedger(pluginId, new LedgerEntry[0]);

    /// <summary>Null means "nothing readable" — missing file (ordinary) or one that failed to parse
    /// (M2). Never throws.</summary>
    private DependencyLedger? TryRead(string gameMini, string pluginId)
    {
        var path = DependencyPaths.LedgerFile(gameMini, pluginId);
        if (!_fs.File.Exists(path)) return null;
        try
        {
            var ledger = JsonSerializer.Deserialize<DependencyLedger>(_fs.File.ReadAllText(path), Json);
            // I2: the file NAME is the only trusted source of PluginId — never whatever the JSON body claims.
            return (ledger ?? new DependencyLedger(pluginId, new LedgerEntry[0])) with { PluginId = pluginId };
        }
        catch (JsonException)
        {
            return null; // M2: a corrupt ledger is skipped, never thrown
        }
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
            .Select(f => _fs.Path.GetFileNameWithoutExtension(f))
            .Where(IsValidPluginId) // M3: a bogus stem is never read, let alone trusted as an id
            .Select(id => TryRead(gameMini, id))
            .Where(l => l is not null)
            .Select(l => l!).ToList(); // M2: a ledger that failed to parse is skipped outright
    }

    /// <summary>M3: a ledger file's stem must look like a plugin id — never empty, ".", "..", or
    /// containing anything outside [A-Za-z0-9._-] — since it is later used to build a parked-file path.</summary>
    private static bool IsValidPluginId(string stem) =>
        stem.Length > 0 && stem != "." && stem != ".."
        && stem.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}

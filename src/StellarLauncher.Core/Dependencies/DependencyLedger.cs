using System;
using System.Collections.Generic;
using System.IO;
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
        TryRead(gameMini, pluginId).Ledger ?? new DependencyLedger(pluginId, new LedgerEntry[0]);

    /// <summary>Like <see cref="Read"/>, but also reports whether a ledger file existed and was unreadable
    /// (Important 2c) — a caller that is about to place files for this plugin should surface that fact.</summary>
    public (DependencyLedger Ledger, bool WasCorrupt) ReadWithStatus(string gameMini, string pluginId)
    {
        var (ledger, corrupt) = TryRead(gameMini, pluginId);
        return (ledger ?? new DependencyLedger(pluginId, new LedgerEntry[0]), corrupt);
    }

    /// <summary>Null Ledger means "nothing readable" — a missing file (ordinary, WasCorrupt false) or one
    /// that failed to parse, didn't have the right shape, or couldn't be read (M2/Important 1,
    /// WasCorrupt true). Never throws.</summary>
    private (DependencyLedger? Ledger, bool WasCorrupt) TryRead(string gameMini, string pluginId)
    {
        var path = DependencyPaths.LedgerFile(gameMini, pluginId);
        if (!_fs.File.Exists(path)) return (null, false);
        try
        {
            var ledger = JsonSerializer.Deserialize<DependencyLedger>(_fs.File.ReadAllText(path), Json);
            if (!IsWellFormed(ledger)) { QuarantineCorrupt(path); return (null, true); } // Important 1
            // I2: the file NAME is the only trusted source of PluginId — never whatever the JSON body claims.
            return (ledger! with { PluginId = pluginId }, false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            QuarantineCorrupt(path); // Important 2(b)
            return (null, true);
        }
    }

    /// <summary>Important 1: System.Text.Json fills a missing/null JSON field with null regardless of a
    /// record's non-nullable annotations, so a well-formed-but-incomplete ledger (e.g. <c>{"PluginId":"x"}</c>)
    /// deserializes without throwing — this is the only check that actually catches it.</summary>
    private static bool IsWellFormed(DependencyLedger? ledger) =>
        ledger is not null && ledger.Entries is not null && ledger.Entries.All(e =>
            e is not null && e.DependencyId is not null && e.Version is not null && e.Files is not null &&
            e.Files.All(f => f is not null && f.Path is not null && f.Sha256 is not null));

    /// <summary>Important 2(b): a corrupt ledger is renamed aside (never deleted) so the evidence survives
    /// the next successful Write to the same plugin id; an existing <c>.corrupt</c> is overwritten.
    /// Best-effort — if even this fails, the ledger is still treated as unreadable by the caller.</summary>
    private void QuarantineCorrupt(string path)
    {
        try { _fs.File.Move(path, path + ".corrupt", overwrite: true); }
        catch { /* best effort */ }
    }

    public void Write(string gameMini, DependencyLedger ledger)
    {
        var path = DependencyPaths.LedgerFile(gameMini, ledger.PluginId);
        if (ledger.Entries.Count == 0) { if (_fs.File.Exists(path)) _fs.File.Delete(path); return; }
        _fs.Directory.CreateDirectory(DependencyPaths.LedgerDir(gameMini));
        var tmp = path + ".tmp";
        _fs.File.WriteAllText(tmp, JsonSerializer.Serialize(ledger, Json));
        _fs.File.Move(tmp, path, overwrite: true); // Important 2(a): atomic — never a half-written ledger
    }

    public IReadOnlyList<DependencyLedger> ReadAll(string gameMini)
    {
        var dir = DependencyPaths.LedgerDir(gameMini);
        if (!_fs.Directory.Exists(dir)) return new DependencyLedger[0];
        return _fs.Directory.GetFiles(dir, "*.json")
            .Select(f => _fs.Path.GetFileNameWithoutExtension(f))
            .Where(IsValidPluginId) // M3: a bogus stem is never read, let alone trusted as an id
            .Select(id => TryRead(gameMini, id).Ledger)
            .Where(l => l is not null)
            .Select(l => l!).ToList(); // M2: a ledger that failed to parse/validate is skipped outright
    }

    /// <summary>M3: a ledger file's stem must look like a plugin id — never empty, ".", "..", or
    /// containing anything outside [A-Za-z0-9._-] — since it is later used to build a parked-file path.</summary>
    private static bool IsValidPluginId(string stem) =>
        stem.Length > 0 && stem != "." && stem != ".."
        && stem.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}

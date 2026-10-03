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

    /// <summary>Minor 3: <paramref name="quarantine"/> is false for read-only callers (Status) that must
    /// not have the side effect of renaming a corrupt ledger aside — they still read it as empty either way.</summary>
    public DependencyLedger Read(string gameMini, string pluginId, bool quarantine = true) =>
        TryRead(gameMini, pluginId, quarantine) ?? new DependencyLedger(pluginId, new LedgerEntry[0]);

    /// <summary>Null means "nothing readable" — a missing file, or one that failed to parse, didn't have
    /// the right shape, or couldn't be read. Never throws. Important 1 (round 4): only a parse failure
    /// (<see cref="JsonException"/>) or a shape failure is quarantined — an <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> is transient/environmental, not evidence the FILE is
    /// corrupt, so it's treated as unreadable for this call only and the file is left exactly where it is.</summary>
    private DependencyLedger? TryRead(string gameMini, string pluginId, bool quarantine)
    {
        var path = DependencyPaths.LedgerFile(gameMini, pluginId);
        if (!_fs.File.Exists(path)) return null;
        try
        {
            var ledger = JsonSerializer.Deserialize<DependencyLedger>(_fs.File.ReadAllText(path), Json);
            if (!IsWellFormed(ledger)) // Important 1 (round 3): wrong shape IS quarantined
            {
                if (quarantine) QuarantineCorrupt(path);
                return null;
            }
            // I2: the file NAME is the only trusted source of PluginId — never whatever the JSON body claims.
            return ledger! with { PluginId = pluginId };
        }
        catch (JsonException)
        {
            if (quarantine) QuarantineCorrupt(path); // Important 2(b) (round 3): parse failure IS quarantined
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // Important 1 (round 4): never quarantined — leave the valid file in place
        }
    }

    /// <summary>Important 1 (round 3): System.Text.Json fills a missing/null JSON field with null
    /// regardless of a record's non-nullable annotations, so a well-formed-but-incomplete ledger (e.g.
    /// <c>{"PluginId":"x"}</c>) deserializes without throwing — this is the only check that catches it.</summary>
    private static bool IsWellFormed(DependencyLedger? ledger) =>
        ledger is not null && ledger.Entries is not null && ledger.Entries.All(e =>
            e is not null && e.DependencyId is not null && e.Version is not null && e.Files is not null &&
            e.Files.All(f => f is not null && f.Path is not null && f.Sha256 is not null));

    /// <summary>Renames a corrupt ledger aside (never deletes it) so the evidence survives the next
    /// successful Write to the same plugin id (a different file); an existing <c>.corrupt</c> is
    /// overwritten. Best-effort — if even this fails, the ledger is still treated as unreadable.</summary>
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
        _fs.File.Move(tmp, path, overwrite: true); // atomic — never a half-written ledger
    }

    public IReadOnlyList<DependencyLedger> ReadAll(string gameMini)
    {
        var dir = DependencyPaths.LedgerDir(gameMini);
        if (!_fs.Directory.Exists(dir)) return new DependencyLedger[0];
        return _fs.Directory.GetFiles(dir, "*.json")
            .Select(f => _fs.Path.GetFileNameWithoutExtension(f))
            .Where(IsValidPluginId) // M3: a bogus stem is never read, let alone trusted as an id
            .Select(id => TryRead(gameMini, id, quarantine: true))
            .Where(l => l is not null)
            .Select(l => l!).ToList(); // a ledger that failed to parse/validate is skipped outright
    }

    /// <summary>M3: a ledger file's stem must look like a plugin id — never empty, ".", "..", or
    /// containing anything outside [A-Za-z0-9._-] — since it is later used to build a parked-file path.</summary>
    private static bool IsValidPluginId(string stem) =>
        stem.Length > 0 && stem != "." && stem != ".."
        && stem.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}

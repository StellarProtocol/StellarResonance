using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StellarLauncher.Core.Dependencies;

/// <summary>One placed file: path relative to game_mini ('/'), its sha256, and whether Vanilla launches park it.</summary>
public sealed record LedgerFile(string Path, string Sha256, bool ModdedOnly);
public sealed record LedgerEntry(string DependencyId, string Version, IReadOnlyList<LedgerFile> Files);

/// <summary><paramref name="Pending"/> (final review I2): a placement in flight, written BEFORE the first file
/// is moved into place and cleared by the ledger write that commits it. If the launcher dies in between, the
/// next run finds the files it already placed listed here with their expected sha256 — a live file that
/// matches is the launcher's own half-finished work, never the player's. Null/absent in a ledger written by
/// an older launcher, and whenever nothing is in flight.
/// <paramref name="Kept"/> (v3 V3): the plugin was removed with "Remove &lt;plugin&gt; only" — its dependencies stay
/// launcher-managed (parked for Vanilla, skipped by the orphan sweep) until removed from its page or adopted again by an
/// ensure after a reinstall. <paramref name="ReinstallRequested"/> (v3 V2): the next ensure removes this plugin's
/// dependency files (hash-checked) and downloads + verifies them afresh, then clears it. Both are omitted from the JSON
/// while false, so a ledger without them is exactly what earlier launchers wrote, and earlier launchers (System.Text.Json
/// skips unknown properties) still read one that has them.</summary>
public sealed record DependencyLedger(string PluginId, IReadOnlyList<LedgerEntry> Entries,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<LedgerEntry>? Pending = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Kept = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool ReinstallRequested = false)
{
    /// <summary>The pending entries other than <paramref name="dependencyId"/>'s, or null when none remain.</summary>
    public IReadOnlyList<LedgerEntry>? PendingWithout(string dependencyId)
    {
        var rest = (Pending ?? Array.Empty<LedgerEntry>()).Where(e => e.DependencyId != dependencyId).ToList();
        return rest.Count == 0 ? null : rest;
    }
}

/// <summary>The record of exactly what the launcher placed for a plugin (so remove deletes exactly that).</summary>
public sealed class DependencyLedgerStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly IFileSystem _fs;
    public DependencyLedgerStore(IFileSystem fs) => _fs = fs;

    /// <summary>Minor 3: <paramref name="quarantine"/> is false for read-only callers (Status) that must
    /// not have the side effect of renaming a corrupt ledger aside — they still read it as empty either way.
    /// Collapses all three <see cref="TryRead"/> outcomes to empty, including present-but-unreadable —
    /// safe here because every caller of this overload only ever reads (ReadAll/FindOwner/Park/Unpark/Status),
    /// never writes back based on what it saw.</summary>
    public DependencyLedger Read(string gameMini, string pluginId, bool quarantine = true) =>
        TryRead(gameMini, pluginId, quarantine).Ledger ?? new DependencyLedger(pluginId, new LedgerEntry[0]);

    /// <summary>Important (round 6): a write path must never treat a PRESENT-but-currently-unreadable
    /// ledger as empty — doing so could overwrite or delete real data the launcher just happens to be
    /// unable to read right now (a transient IO/permission error), unlike a ledger that's genuinely
    /// missing or one that was corrupt and has already been safely quarantined aside (in both of those
    /// cases there is nothing left to lose by proceeding as if empty). Returns false — and an empty ledger
    /// the caller must NOT act on — only for the present-but-unreadable case.</summary>
    public bool TryReadForWrite(string gameMini, string pluginId, out DependencyLedger ledger)
    {
        var (result, unreadable) = TryRead(gameMini, pluginId, quarantine: true);
        ledger = result ?? new DependencyLedger(pluginId, new LedgerEntry[0]);
        return !unreadable;
    }

    /// <summary>Ledger is null for "nothing readable" in every case (missing, corrupt-and-quarantined, or
    /// present-but-unreadable). Unreadable distinguishes the last of those three — present on disk, but an
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> (transient/environmental)
    /// stopped THIS read; never set for a parse/shape failure, since quarantining already moved the bad
    /// data safely aside. Never throws.</summary>
    private (DependencyLedger? Ledger, bool Unreadable) TryRead(string gameMini, string pluginId, bool quarantine)
    {
        var path = DependencyPaths.LedgerFile(gameMini, pluginId);
        if (!_fs.File.Exists(path)) return (null, false); // missing — safe to treat as empty
        try
        {
            var ledger = JsonSerializer.Deserialize<DependencyLedger>(_fs.File.ReadAllText(path), Json);
            if (!IsWellFormed(ledger)) // Important 1 (round 3): wrong shape IS quarantined
            {
                if (quarantine) QuarantineCorrupt(path);
                return (null, false); // bad data safely moved aside — safe to treat as empty
            }
            // I2: the file NAME is the only trusted source of PluginId — never whatever the JSON body claims.
            return (ledger! with { PluginId = pluginId }, false);
        }
        catch (JsonException)
        {
            if (quarantine) QuarantineCorrupt(path); // Important 2(b) (round 3): parse failure IS quarantined
            return (null, false); // bad data safely moved aside — safe to treat as empty
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, true); // present, but unreadable RIGHT NOW — NOT safe to treat as empty
        }
    }

    /// <summary>Important 1 (round 3): System.Text.Json fills a missing/null JSON field with null
    /// regardless of a record's non-nullable annotations, so a well-formed-but-incomplete ledger (e.g.
    /// <c>{"PluginId":"x"}</c>) deserializes without throwing — this is the only check that catches it.</summary>
    private static bool IsWellFormed(DependencyLedger? ledger) =>
        ledger is not null && ledger.Entries is not null && ledger.Entries.All(IsWellFormed)
        && (ledger.Pending is null || ledger.Pending.All(IsWellFormed));

    private static bool IsWellFormed(LedgerEntry? e) =>
        e is not null && e.DependencyId is not null && e.Version is not null && e.Files is not null &&
        e.Files.All(f => f is not null && f.Path is not null && f.Sha256 is not null);

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
        if (ledger.Entries.Count == 0 && ledger.Pending is not { Count: > 0 })
        {
            if (_fs.File.Exists(path)) _fs.File.Delete(path);
            return;
        }
        _fs.Directory.CreateDirectory(DependencyPaths.LedgerDir(gameMini));
        var tmp = path + ".tmp";
        _fs.File.WriteAllText(tmp, JsonSerializer.Serialize(ledger, Json));
        _fs.File.Move(tmp, path, overwrite: true); // atomic — never a half-written ledger
    }

    /// <summary>Ids with a ledger file on disk — file stems only, validated like <see cref="ReadAll"/>;
    /// no ledger is read, so this never quarantines anything.</summary>
    public IReadOnlyList<string> PluginIds(string gameMini)
    {
        var dir = DependencyPaths.LedgerDir(gameMini);
        if (!_fs.Directory.Exists(dir)) return Array.Empty<string>();
        return _fs.Directory.GetFiles(dir, "*.json")
            .Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(f => _fs.Path.GetFileNameWithoutExtension(f))
            .Where(DependencyPaths.IsValidPluginId)
            .OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    /// <summary><paramref name="quarantine"/> false for read-only callers (Status's ownership check), which
    /// must not rename a corrupt ledger aside — they still skip it either way.</summary>
    public IReadOnlyList<DependencyLedger> ReadAll(string gameMini, bool quarantine = true)
    {
        var dir = DependencyPaths.LedgerDir(gameMini);
        if (!_fs.Directory.Exists(dir)) return new DependencyLedger[0];
        return _fs.Directory.GetFiles(dir, "*.json")
            .Select(f => _fs.Path.GetFileNameWithoutExtension(f))
            .Where(DependencyPaths.IsValidPluginId) // M3: a bogus stem is never read, let alone trusted as an id
            .Select(id => TryRead(gameMini, id, quarantine).Ledger)
            .Where(l => l is not null)
            .Select(l => l!).ToList(); // a ledger that failed to parse/validate/read is skipped outright
    }
}

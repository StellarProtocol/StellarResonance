using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;

namespace StellarLauncher.Core.Dependencies;

/// <summary>Moves <c>moddedOnly</c> dependency files out of the game tree for a vanilla launch, and back
/// afterwards (rule 8 of the dependency-service contract). Operates across every plugin's ledger.
/// I2: every recorded path is validated via <see cref="DependencyPaths.FromLedger"/> before use. I3: a
/// file whose content no longer matches the recorded hash was replaced by the user or another tool and
/// is left where it is.</summary>
public sealed class DependencyParking
{
    private readonly IFileSystem _fs;
    private readonly DependencyLedgerStore _store;

    public DependencyParking(IFileSystem fs, DependencyLedgerStore store)
    {
        _fs = fs;
        _store = store;
    }

    /// <summary>Moves every present, untouched <c>moddedOnly</c> file to its parked path. Idempotent — a
    /// file already parked (or never placed) is left alone.</summary>
    public void Park(string gameMini)
    {
        foreach (var ledger in _store.ReadAll(gameMini)) Park(gameMini, ledger);
    }

    /// <summary>Moves parked files back, unless something now occupies the destination — in which case
    /// it stays parked. Idempotent — a file with nothing parked is left alone. Final review I6: a plugin in
    /// <paramref name="keepParked"/> (disabled) is parked instead, exactly as for a vanilla launch.</summary>
    public void Unpark(string gameMini, IReadOnlySet<string>? keepParked = null)
    {
        foreach (var ledger in _store.ReadAll(gameMini))
        {
            if (keepParked?.Contains(ledger.PluginId) == true) { Park(gameMini, ledger); continue; }
            foreach (var f in ModdedOnlyFiles(ledger))
            {
                var abs = DependencyPaths.FromLedger(gameMini, f.Path);
                if (abs is null) continue;
                var parked = DependencyPaths.ParkedPath(gameMini, ledger.PluginId, f.Path);
                if (!_fs.File.Exists(parked) || _fs.File.Exists(abs)) continue;
                Move(parked, abs);
            }
        }
    }

    private void Park(string gameMini, DependencyLedger ledger)
    {
        foreach (var f in ModdedOnlyFiles(ledger))
        {
            var abs = DependencyPaths.FromLedger(gameMini, f.Path);
            if (abs is null || !DependencyFileHash.Matches(_fs, abs, f.Sha256)) continue;
            Move(abs, DependencyPaths.ParkedPath(gameMini, ledger.PluginId, f.Path));
        }
    }

    private static IEnumerable<LedgerFile> ModdedOnlyFiles(DependencyLedger ledger) =>
        ledger.Entries.SelectMany(e => e.Files).Where(f => f.ModdedOnly);

    private void Move(string from, string to)
    {
        var dir = _fs.Path.GetDirectoryName(to);
        if (!string.IsNullOrEmpty(dir)) _fs.Directory.CreateDirectory(dir);
        _fs.File.Move(from, to, overwrite: true);
    }
}

using System.IO.Abstractions;
using System.Linq;

namespace StellarLauncher.Core.Dependencies;

/// <summary>Moves <c>moddedOnly</c> dependency files out of the game tree for a vanilla launch, and back
/// afterwards (rule 8 of the dependency-service contract). Operates across every plugin's ledger.</summary>
public sealed class DependencyParking
{
    private readonly IFileSystem _fs;
    private readonly DependencyLedgerStore _store;

    public DependencyParking(IFileSystem fs, DependencyLedgerStore store)
    {
        _fs = fs;
        _store = store;
    }

    /// <summary>Moves every present <c>moddedOnly</c> file to its parked path. Idempotent — a file
    /// already parked (or never placed) is left alone.</summary>
    public void Park(string gameMini)
    {
        foreach (var ledger in _store.ReadAll(gameMini))
        foreach (var f in ledger.Entries.SelectMany(e => e.Files).Where(f => f.ModdedOnly))
        {
            var abs = _fs.Path.Combine(gameMini, f.Path);
            if (!_fs.File.Exists(abs)) continue;
            Move(abs, DependencyPaths.ParkedPath(gameMini, ledger.PluginId, f.Path));
        }
    }

    /// <summary>Moves parked files back, unless something now occupies the destination — in which case
    /// it stays parked. Idempotent — a file with nothing parked is left alone.</summary>
    public void Unpark(string gameMini)
    {
        foreach (var ledger in _store.ReadAll(gameMini))
        foreach (var f in ledger.Entries.SelectMany(e => e.Files).Where(f => f.ModdedOnly))
        {
            var abs = _fs.Path.Combine(gameMini, f.Path);
            var parked = DependencyPaths.ParkedPath(gameMini, ledger.PluginId, f.Path);
            if (!_fs.File.Exists(parked) || _fs.File.Exists(abs)) continue;
            Move(parked, abs);
        }
    }

    private void Move(string from, string to)
    {
        var dir = _fs.Path.GetDirectoryName(to);
        if (!string.IsNullOrEmpty(dir)) _fs.Directory.CreateDirectory(dir);
        _fs.File.Move(from, to, overwrite: true);
    }
}

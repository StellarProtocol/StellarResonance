using System;
using System.IO.Abstractions;
using System.Linq;

namespace StellarLauncher.Core.Services;

/// <summary>A point-in-time view of the BepInEx interop assembly directory.</summary>
/// <param name="Count">Number of generated <c>*.dll</c> interop assemblies present.</param>
/// <param name="NewestWriteUtc">Most recent write time across those assemblies, or null when none exist.</param>
public readonly record struct InteropSnapshot(int Count, DateTimeOffset? NewestWriteUtc);

/// <summary>
/// Observes BepInEx's IL2CPP interop assembly directory so the launcher can give feedback during the
/// silent 2-3 min Il2CppInterop (re)generation that runs on first launch and after a game update. With
/// the BepInEx console off (prod), this directory is the only reliable progress signal — the disk log
/// is buffered (InstantFlushing=false) and won't update in real time.
/// </summary>
public interface IInteropWatch
{
    /// <summary>True when BepInEx is likely to (re)generate interop assemblies on the next launch: the
    /// interop dir is missing/empty, or the game's IL2CPP source files are newer than the newest
    /// generated assembly (a heuristic for "the game updated since the last generation"). False
    /// positives degrade gracefully — the monitor just settles quickly; false negatives fall back to
    /// today's silent behaviour.</summary>
    bool RegenExpected(string gameMiniDir);

    /// <summary>A point-in-time view of the interop directory (assembly count + newest write time).</summary>
    InteropSnapshot Snapshot(string gameMiniDir);
}

public sealed class InteropWatch : IInteropWatch
{
    private readonly IFileSystem _fs;
    public InteropWatch(IFileSystem fs) => _fs = fs;

    public bool RegenExpected(string gameMiniDir)
    {
        var dlls = InteropDlls(gameMiniDir);
        if (dlls.Length == 0) return true;   // first run — nothing generated yet

        var newestInterop = dlls.Max(f => _fs.File.GetLastWriteTimeUtc(f));
        foreach (var src in SourceFiles(gameMiniDir))
            if (_fs.File.Exists(src) && _fs.File.GetLastWriteTimeUtc(src) > newestInterop)
                return true;   // game patched since the last generation
        return false;
    }

    public InteropSnapshot Snapshot(string gameMiniDir)
    {
        var dlls = InteropDlls(gameMiniDir);
        if (dlls.Length == 0) return new InteropSnapshot(0, null);
        var newest = dlls.Max(f => _fs.File.GetLastWriteTimeUtc(f));
        return new InteropSnapshot(dlls.Length, new DateTimeOffset(newest, TimeSpan.Zero));
    }

    private string[] InteropDlls(string gameMiniDir)
    {
        var dir = _fs.Path.Combine(gameMiniDir, "BepInEx", "interop");
        return _fs.Directory.Exists(dir) ? _fs.Directory.GetFiles(dir, "*.dll") : Array.Empty<string>();
    }

    // The IL2CPP inputs BepInEx hashes to decide whether to regenerate. GameAssembly.dll sits at the
    // install root in every layout (StarLauncher + flat/Steam); global-metadata is best-effort for the
    // official layout. Either being newer than the interop set means a regeneration is due.
    private string[] SourceFiles(string gameMiniDir) => new[]
    {
        _fs.Path.Combine(gameMiniDir, "GameAssembly.dll"),
        _fs.Path.Combine(gameMiniDir, "StarSEA_Data", "il2cpp_data", "Metadata", "global-metadata.dat"),
    };
}

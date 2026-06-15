using System;
using System.Collections.Generic;
using System.IO.Abstractions;

namespace StellarLauncher.Core.Services;

/// <summary>
/// Finds game_mini directories by probing a set of search roots (Wine prefixes on Linux,
/// drive roots on Windows) for the StarLauncher install layout. The search roots are
/// supplied by the composition root; the probing logic is what's unit-tested here.
/// </summary>
public sealed class GameDetector : IGameDetector
{
    // Under a Wine prefix: <root>/drive_c/Star/StarLauncher/game/release_*/game_mini
    private static readonly string[] WineSuffix = { "drive_c", "Star", "StarLauncher", "game" };
    // Native Windows: <root>/Star/StarLauncher/game/release_*/game_mini
    private static readonly string[] NativeSuffix = { "Star", "StarLauncher", "game" };

    private readonly IFileSystem _fs;
    private readonly IGameLocator _locator;
    private readonly Func<IReadOnlyList<string>> _searchRoots;
    private readonly Func<IReadOnlyList<string>> _runnerCandidates;
    private readonly Func<IReadOnlyList<string>> _umuCandidates;

    public GameDetector(IFileSystem fs, IGameLocator locator,
        Func<IReadOnlyList<string>> searchRoots,
        Func<IReadOnlyList<string>>? runnerCandidates = null,
        Func<IReadOnlyList<string>>? umuCandidates = null)
    {
        _fs = fs;
        _locator = locator;
        _searchRoots = searchRoots;
        _runnerCandidates = runnerCandidates ?? (() => System.Array.Empty<string>());
        _umuCandidates = umuCandidates ?? (() => System.Array.Empty<string>());
    }

    /// <summary>First existing umu-run launcher binary (preferred for Proton), or null.</summary>
    public string? DetectUmu()
    {
        foreach (var candidate in _umuCandidates())
            if (_fs.File.Exists(candidate)) return candidate;
        return null;
    }

    /// <summary>
    /// Derive the WINEPREFIX from a game_mini path (the segment before <c>/drive_c/</c>).
    /// Returns null for a native-Windows path (no Wine prefix).
    /// </summary>
    public static string? WinePrefixFor(string gameMini)
    {
        foreach (var marker in new[] { "/drive_c/", "\\drive_c\\" })
        {
            var i = gameMini.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i > 0) return gameMini.Substring(0, i);
        }
        return null;
    }

    /// <summary>First existing Wine/Proton runner binary from the priority-ordered candidates, or null.</summary>
    public string? DetectRunner()
    {
        foreach (var candidate in _runnerCandidates())
            if (_fs.File.Exists(candidate)) return candidate;
        return null;
    }

    public IReadOnlyList<string> Detect()
    {
        var found = new List<string>();
        foreach (var root in _searchRoots())
        {
            foreach (var suffix in new[] { WineSuffix, NativeSuffix })
            {
                var parts = new List<string>(suffix.Length + 1) { root };
                parts.AddRange(suffix);
                var gameRoot = _fs.Path.Combine(parts.ToArray());
                var gameMini = _locator.FindGameMini(gameRoot);
                if (gameMini is not null && !found.Contains(gameMini))
                    found.Add(gameMini);
            }
        }
        return found;
    }
}

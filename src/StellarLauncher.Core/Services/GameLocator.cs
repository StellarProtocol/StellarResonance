// src/StellarLauncher.Core/Services/GameLocator.cs
using System;
using System.IO.Abstractions;
using System.Linq;

namespace StellarLauncher.Core.Services;

public sealed class GameLocator : IGameLocator
{
    private readonly IFileSystem _fs;
    public GameLocator(IFileSystem fs) => _fs = fs;

    public string? FindGameMini(string gameRoot)
    {
        if (!_fs.Directory.Exists(gameRoot)) return null;

        return _fs.Directory.GetDirectories(gameRoot)
            .Where(d => _fs.Path.GetFileName(d).StartsWith("release_", StringComparison.Ordinal))
            .Select(d => _fs.Path.Combine(d, "game_mini"))
            .Where(p => _fs.Directory.Exists(p))
            .OrderByDescending(p => ReleaseVersion(_fs.Path.GetFileName(_fs.Path.GetDirectoryName(p)!)))
            .FirstOrDefault();
    }

    /// <summary>"release_2.11" → comparable Version(2.11); unparseable → 0.0.</summary>
    private static Version ReleaseVersion(string dirName)
    {
        var raw = dirName.StartsWith("release_", StringComparison.Ordinal)
            ? dirName["release_".Length..] : dirName;
        return Version.TryParse(raw, out var v) ? v : new Version(0, 0);
    }
}

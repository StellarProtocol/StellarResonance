// src/StellarLauncher.Core/Services/Installer.cs
using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace StellarLauncher.Core.Services;

public sealed class Installer : IInstaller
{
    private const string VersionFile =
        "BepInEx/plugins/Stellar.Framework/.stellar-version";

    private readonly IFileSystem _fs;
    public Installer(IFileSystem fs) => _fs = fs;

    public async Task InstallAsync(Stream bundleZip, string expectedSha256,
        string gameMiniDir, string version, CancellationToken ct = default)
    {
        // Buffer to memory so we can verify before touching disk.
        using var buffer = new MemoryStream();
        await bundleZip.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var actual = Convert.ToHexString(await SHA256.HashDataAsync(buffer, ct)).ToLowerInvariant();
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"bundle sha256 mismatch (expected {expectedSha256}, got {actual})");

        buffer.Position = 0;
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        var root = _fs.Path.GetFullPath(gameMiniDir);
        var rootPrefix = root.EndsWith(_fs.Path.DirectorySeparatorChar)
            ? root : root + _fs.Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
            var dest = _fs.Path.GetFullPath(_fs.Path.Combine(gameMiniDir, entry.FullName));
            if (!dest.StartsWith(rootPrefix, StringComparison.Ordinal) &&
                !string.Equals(dest, root, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"zip entry escapes target directory: {entry.FullName}");
            _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(dest)!);
            using var src = entry.Open();
            using var outStream = _fs.File.Create(dest);
            await src.CopyToAsync(outStream, ct);
        }

        _fs.File.WriteAllText(_fs.Path.Combine(gameMiniDir, VersionFile), version);
    }

    public string? ReadInstalledVersion(string gameMiniDir)
    {
        var path = _fs.Path.Combine(gameMiniDir, VersionFile);
        return _fs.File.Exists(path) ? _fs.File.ReadAllText(path).Trim() : null;
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace StellarLauncher.Core.Services;

public sealed class LauncherSelfUpdater : ILauncherSelfUpdater
{
    private readonly IFileSystem _fs;
    public LauncherSelfUpdater(IFileSystem fs) => _fs = fs;

    public async Task StageAsync(Stream zip, string expectedSha256, string stagingDir, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await zip.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(buffer, ct)).ToLowerInvariant();
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"launcher sha256 mismatch (expected {expectedSha256}, got {actual})");

        var root = _fs.Path.GetFullPath(stagingDir);
        var rootPrefix = root.EndsWith(_fs.Path.DirectorySeparatorChar) ? root : root + _fs.Path.DirectorySeparatorChar;
        _fs.Directory.CreateDirectory(stagingDir);
        buffer.Position = 0;
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var dest = _fs.Path.GetFullPath(_fs.Path.Combine(stagingDir, entry.FullName));
            if (!dest.StartsWith(rootPrefix, StringComparison.Ordinal) &&
                !string.Equals(dest, root, StringComparison.Ordinal))
                throw new InvalidDataException($"zip entry escapes staging dir: {entry.FullName}");
            _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(dest)!);
            using var src = entry.Open();
            using var outStream = _fs.File.Create(dest);
            await src.CopyToAsync(outStream, ct);
        }
    }

    public string BuildWindowsSwapScript(string stagingDir, string installDir, string exeName) =>
        "@echo off\r\n" +
        "timeout /t 2 /nobreak >NUL\r\n" +
        $"robocopy \"{stagingDir}\" \"{installDir}\" /E /NFL /NDL /NJH /NJS /NP >NUL\r\n" +
        $"start \"\" \"{_fs.Path.Combine(installDir, exeName)}\"\r\n" +
        "del \"%~f0\"\r\n";

    public void ApplyAndRestart(string stagingDir, string installDir, string exeName, bool isWindows)
    {
        if (isWindows)
        {
            var script = BuildWindowsSwapScript(stagingDir, installDir, exeName);
            var scriptPath = _fs.Path.Combine(_fs.Path.GetTempPath(), "stellar-launcher-update.cmd");
            _fs.File.WriteAllText(scriptPath, script);
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
                { UseShellExecute = true, CreateNoWindow = true });
            Environment.Exit(0);
        }
        else
        {
            CopyDir(stagingDir, installDir);
            var exe = _fs.Path.Combine(installDir, exeName);
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(exe,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
                catch { /* best effort */ }
            }
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
            Environment.Exit(0);
        }
    }

    private void CopyDir(string from, string to)
    {
        foreach (var dir in _fs.Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            _fs.Directory.CreateDirectory(dir.Replace(from, to));
        foreach (var file in _fs.Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(from, to);
            _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(target)!);
            _fs.File.Copy(file, target, overwrite: true);
        }
    }
}

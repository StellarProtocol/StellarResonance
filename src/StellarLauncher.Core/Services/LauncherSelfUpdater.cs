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
            SwapInPlace(stagingDir, installDir, exeName);
            var exe = _fs.Path.Combine(installDir, exeName);
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
            Environment.Exit(0);
        }
    }

    /// <summary>
    /// Replaces <paramref name="installDir"/> with the contents of <paramref name="stagingDir"/>, swapping
    /// the running executable by rename. Overwriting a live ELF in place fails with ETXTBSY ("text file
    /// busy"); renaming it aside while it executes is allowed (the inode stays live). The new binary is
    /// fully written as *.new first, so the old binary survives intact if anything before the swap fails.
    /// Does NOT restart — extracted from <see cref="ApplyAndRestart"/> so it can be tested.
    /// </summary>
    public void SwapInPlace(string stagingDir, string installDir, string exeName)
    {
        var exe = _fs.Path.Combine(installDir, exeName);
        var newExe = exe + ".new";
        var oldExe = exe + ".old";

        // Everything EXCEPT the running executable can be copied straight into place.
        CopyDir(stagingDir, installDir, skip: exeName);

        _fs.File.Copy(_fs.Path.Combine(stagingDir, exeName), newExe, overwrite: true);
        if (_fs.File.Exists(oldExe)) { try { _fs.File.Delete(oldExe); } catch { /* best effort */ } }
        if (_fs.File.Exists(exe)) _fs.File.Move(exe, oldExe);
        _fs.File.Move(newExe, exe);

        MakeExecutable(exe);
        // Keep the desktop-integration helpers runnable after an in-place update.
        foreach (var script in new[] { "install.sh", "uninstall.sh" })
        {
            var p = _fs.Path.Combine(installDir, script);
            if (_fs.File.Exists(p)) MakeExecutable(p);
        }
    }

    public void CleanupStaleUpdate(string installDir, string exeName)
    {
        // The renamed-aside binary from a prior update can only be removed once the old process
        // (which still held it open) has exited — i.e. on the next startup, here.
        foreach (var leftover in new[] { exeName + ".old", exeName + ".new" })
        {
            var p = _fs.Path.Combine(installDir, leftover);
            try { if (_fs.File.Exists(p)) _fs.File.Delete(p); } catch { /* best effort */ }
        }
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch { /* best effort */ }
    }

    private void CopyDir(string from, string to, string? skip = null)
    {
        foreach (var dir in _fs.Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            _fs.Directory.CreateDirectory(dir.Replace(from, to));
        foreach (var file in _fs.Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            if (skip is not null && _fs.Path.GetFileName(file) == skip) continue;
            var target = file.Replace(from, to);
            _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(target)!);
            _fs.File.Copy(file, target, overwrite: true);
        }
    }
}

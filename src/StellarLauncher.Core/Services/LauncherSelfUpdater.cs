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

    /// <summary>
    /// POSIX equivalent of the Windows swap script. The running process must NOT copy over its own files:
    /// the executable can't be reopened for writing (ETXTBSY) and overwriting an in-use mmap'd library
    /// (libSkiaSharp.so) invalidates its pages and SIGBUSes the live process mid-copy. So an external shell
    /// waits for the launcher (<paramref name="pid"/>) to exit, then copies staging→install and relaunches.
    /// </summary>
    public string BuildUnixSwapScript(string stagingDir, string installDir, string exeName, int pid)
    {
        var exe = _fs.Path.Combine(installDir, exeName);
        return
            "#!/bin/sh\n" +
            $"while kill -0 {pid} 2>/dev/null; do sleep 0.1; done\n" +   // wait for the old launcher to exit
            $"cp -a \"{stagingDir}/.\" \"{installDir}/\"\n" +            // now safe — nothing is mapped
            $"chmod +x \"{exe}\" \"{installDir}/install.sh\" \"{installDir}/uninstall.sh\" 2>/dev/null || true\n" +
            $"\"{exe}\" &\n" +                                           // relaunch (detaches when we exit)
            "rm -f \"$0\"\n";
    }

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
            var script = BuildUnixSwapScript(stagingDir, installDir, exeName, Environment.ProcessId);
            var scriptPath = _fs.Path.Combine(_fs.Path.GetTempPath(), "stellar-launcher-update.sh");
            _fs.File.WriteAllText(scriptPath, script);
            // Detach via setsid where available so the swapper survives this process exiting.
            Process.Start(new ProcessStartInfo("/bin/sh", $"-c \"setsid /bin/sh '{scriptPath}' >/dev/null 2>&1 </dev/null || /bin/sh '{scriptPath}'\"")
                { UseShellExecute = false });
            Environment.Exit(0);
        }
    }

    public void CleanupStaleUpdate(string installDir, string exeName)
    {
        // Remove partial binaries left by an interrupted update (older builds renamed the exe aside).
        foreach (var leftover in new[] { exeName + ".old", exeName + ".new" })
        {
            var p = _fs.Path.Combine(installDir, leftover);
            try { if (_fs.File.Exists(p)) _fs.File.Delete(p); } catch { /* best effort */ }
        }
    }
}

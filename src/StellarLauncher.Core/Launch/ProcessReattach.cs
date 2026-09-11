using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using StellarLauncher.Core.Clients;

namespace StellarLauncher.Core.Launch;

public sealed record RunningProcess(int Pid, string CommandLine);

public interface IRunningProcessScanner
{
    IReadOnlyList<RunningProcess> Snapshot();
}

/// <summary>Linux: /proc/&lt;pid&gt;/cmdline (NUL-separated). Wine processes carry the Z:\ path of the exe.</summary>
public sealed class ProcFsScanner : IRunningProcessScanner
{
    private readonly IFileSystem _fs;
    public ProcFsScanner(IFileSystem fs) => _fs = fs;

    public IReadOnlyList<RunningProcess> Snapshot()
    {
        var result = new List<RunningProcess>();
        if (!_fs.Directory.Exists("/proc")) return result;
        foreach (var dir in _fs.Directory.GetDirectories("/proc"))
        {
            if (!int.TryParse(_fs.Path.GetFileName(dir), out var pid)) continue;
            var file = _fs.Path.Combine(dir, "cmdline");
            try
            {
                if (!_fs.File.Exists(file)) continue;
                var text = Encoding.UTF8.GetString(_fs.File.ReadAllBytes(file)).Replace('\0', ' ').Trim();
                if (text.Length > 0) result.Add(new RunningProcess(pid, text));
            }
            catch { /* process vanished or unreadable — skip */ }
        }
        return result;
    }
}

/// <summary>Windows: main-module path of every process we may read; access-denied processes are skipped.</summary>
public sealed class WindowsProcessScanner : IRunningProcessScanner
{
    public IReadOnlyList<RunningProcess> Snapshot()
    {
        var result = new List<RunningProcess>();
        foreach (var p in Process.GetProcesses())
        {
            try { if (p.MainModule?.FileName is { } f) result.Add(new RunningProcess(p.Id, f)); }
            catch { /* protected / exited */ }
            finally { p.Dispose(); }
        }
        return result;
    }
}

public static class ProcessReattach
{
    /// <summary>The install root a game process's command line will contain: the StarLauncher dir for the
    /// official layouts (the launcher exe lives there), else game_mini itself (Steam flat).</summary>
    public static string ClientRoot(string gameMiniDir)
    {
        var norm = ClientCandidates.NormalizePath(gameMiniDir);
        var segs = norm.Split('/');
        var idx = Array.FindLastIndex(segs, s => string.Equals(s, "StarLauncher", StringComparison.OrdinalIgnoreCase));
        return idx < 0 ? norm : string.Join('/', segs.Take(idx + 1));
    }

    public static bool Matches(string commandLine, string gameMiniDir)
    {
        var needle = Canon(ClientRoot(gameMiniDir));
        var hay = Canon(commandLine);
        var i = hay.IndexOf(needle, StringComparison.Ordinal);
        // must end at a path boundary so ".../BlueProtocol" never matches ".../BlueProtocol2"
        return i >= 0 && (i + needle.Length == hay.Length || hay[i + needle.Length] is '/' or ' ' or '"');
    }

    public static int? Find(ClientProfile client, IRunningProcessScanner scanner) =>
        scanner.Snapshot().FirstOrDefault(p => Matches(p.CommandLine, client.GameMiniDir))?.Pid;

    // lower-case, forward slashes, Wine's Z: drive collapsed onto the root
    private static string Canon(string s) => s.Replace('\\', '/').ToLowerInvariant().Replace("z:/", "/");
}

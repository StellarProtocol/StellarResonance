using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StellarLauncher.Core.Services;

/// <summary>
/// The interpreter + arguments used to run a user pre/post-launch script on the current OS. Pure and
/// testable (the OS is a parameter, not <see cref="OperatingSystem"/>) so the per-platform choice can be
/// asserted on any CI host. On Linux/macOS a script always runs through <c>/bin/sh &lt;path&gt;</c> — it is
/// executed as a POSIX sh script regardless of shebang or execute bit. On Windows there is no
/// <c>/bin/sh</c>, so the interpreter is chosen from the file extension.
/// </summary>
public readonly record struct ScriptCommand(string FileName, IReadOnlyList<string> Args)
{
    public static ScriptCommand For(string scriptPath, bool isWindows)
    {
        if (!isWindows)
            return new ScriptCommand("/bin/sh", new[] { scriptPath });

        // Windows: pick the interpreter by extension. PowerShell runs with a bypass policy + no profile so
        // an unsigned user script actually runs; .bat/.cmd go through cmd; a real executable runs directly;
        // anything else falls back to cmd, which honours PATHEXT associations.
        var ext = Path.GetExtension(scriptPath).ToLowerInvariant();
        return ext switch
        {
            ".ps1" => new ScriptCommand("powershell.exe",
                new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath }),
            ".bat" or ".cmd" => new ScriptCommand("cmd.exe", new[] { "/c", scriptPath }),
            ".exe" or ".com" => new ScriptCommand(scriptPath, Array.Empty<string>()),
            _ => new ScriptCommand("cmd.exe", new[] { "/c", scriptPath }),
        };
    }
}

/// <summary>Runs a user pre/post-launch script through the right interpreter for the OS (see
/// <see cref="ScriptCommand"/>): <c>/bin/sh</c> on Linux/macOS, an extension-chosen interpreter on Windows.</summary>
public static class LaunchScripts
{
    private static ProcessStartInfo BuildStartInfo(string scriptPath, IEnumerable<KeyValuePair<string, string?>> env)
    {
        var cmd = ScriptCommand.For(scriptPath, OperatingSystem.IsWindows());
        var psi = new ProcessStartInfo { FileName = cmd.FileName, UseShellExecute = false };
        foreach (var a in cmd.Args) psi.ArgumentList.Add(a);
        foreach (var kv in env)
            if (kv.Value is not null) psi.Environment[kv.Key] = kv.Value;
        return psi;
    }

    public static async Task<int?> RunAsync(string scriptPath, IEnumerable<KeyValuePair<string, string?>> env,
        TimeSpan timeout, CancellationToken ct = default)
    {
        using var proc = TryStart(BuildStartInfo(scriptPath, env));
        if (proc is null) return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
            return proc.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return null;
        }
    }

    /// <summary>Start the script without waiting and return a handle whose Kill ends it (and its tree) when the
    /// game exits. Null if the process could not be started.</summary>
    public static IScriptHandle? Start(string scriptPath, IEnumerable<KeyValuePair<string, string?>> env)
    {
        var proc = TryStart(BuildStartInfo(scriptPath, env));
        return proc is null ? null : new ProcessHandle(proc);
    }

    // A missing interpreter (e.g. no /bin/sh, or powershell.exe absent) throws Win32Exception rather than
    // returning null — treat that as "could not start" so a bad script never crashes the launch flow.
    private static Process? TryStart(ProcessStartInfo psi)
    {
        try { return Process.Start(psi); }
        catch (Exception) { return null; }
    }

    private sealed class ProcessHandle(Process proc) : IScriptHandle
    {
        public bool HasExited { get { try { return proc.HasExited; } catch { return true; } } }
        public void Kill() { try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* best effort */ } }
        public void Dispose() { try { proc.Dispose(); } catch { /* best effort */ } }
    }
}

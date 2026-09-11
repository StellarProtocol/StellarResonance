using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace StellarLauncher.Core.Services;

/// <summary>Runs a user pre/post-launch script via <c>/bin/sh &lt;path&gt;</c> (Linux only). The file is executed as a POSIX sh script — a non-sh shebang is not honored and no execute bit is required.</summary>
public static class LaunchScripts
{
    public static async Task<int?> RunAsync(string scriptPath, IEnumerable<KeyValuePair<string, string?>> env,
        TimeSpan timeout, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo { FileName = "/bin/sh", UseShellExecute = false };
        psi.ArgumentList.Add(scriptPath);
        foreach (var kv in env)
            if (kv.Value is not null) psi.Environment[kv.Key] = kv.Value;

        using var proc = Process.Start(psi);
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
    /// game exits. Linux only, via <c>/bin/sh &lt;path&gt;</c>. Null if the process could not be started.</summary>
    public static IScriptHandle? Start(string scriptPath, IEnumerable<KeyValuePair<string, string?>> env)
    {
        var psi = new ProcessStartInfo { FileName = "/bin/sh", UseShellExecute = false };
        psi.ArgumentList.Add(scriptPath);
        foreach (var kv in env)
            if (kv.Value is not null) psi.Environment[kv.Key] = kv.Value;

        var proc = Process.Start(psi);
        return proc is null ? null : new ProcessHandle(proc);
    }

    private sealed class ProcessHandle(Process proc) : IScriptHandle
    {
        public bool HasExited { get { try { return proc.HasExited; } catch { return true; } } }
        public void Kill() { try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* best effort */ } }
        public void Dispose() { try { proc.Dispose(); } catch { /* best effort */ } }
    }
}

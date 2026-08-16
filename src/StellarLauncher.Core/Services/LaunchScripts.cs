using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace StellarLauncher.Core.Services;

/// <summary>Runs a user pre/post-launch shell script via /bin/sh (Linux advanced launch options).</summary>
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
}

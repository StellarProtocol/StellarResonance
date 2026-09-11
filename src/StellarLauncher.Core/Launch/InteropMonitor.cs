using System;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Services;

namespace StellarLauncher.Core.Launch;

/// <summary>Drives per-client progress while BepInEx regenerates IL2CPP interop assemblies (2–3 min, no window).
/// Phase 1: nothing written since launch → indeterminate. Phase 2: assemblies landing → N/~estimate.
/// Done when writes settle for 10 s; bounded by a 600 s cap and the process exiting.</summary>
public sealed class InteropMonitor
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(600);
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(1);
    private const int DefaultEstimate = 190;

    private readonly IInteropWatch _watch;
    private readonly LaunchEnvironment _env;

    public InteropMonitor(IInteropWatch watch, LaunchEnvironment env) { _watch = watch; _env = env; }

    public bool RegenExpected(string gameMini) => _watch.RegenExpected(gameMini);

    /// <returns>The settled assembly count (store as LastInteropCount), or null if the process exited / timed out.</returns>
    public async Task<int?> RunAsync(string gameMini, IGameProcess proc, int estimate, IProgress<LaunchEvent> events, CancellationToken ct)
    {
        var target = estimate > 0 ? estimate : DefaultEstimate;
        var launchedAt = _env.Now;
        DateTimeOffset? genStartedAt = null;
        while (true)
        {
            var snap = _watch.Snapshot(gameMini);
            var freshWrite = snap.NewestWriteUtc is { } nw && nw >= launchedAt;
            if (genStartedAt is null && !freshWrite)
                ReportPhase1(events, _env.Now - launchedAt);
            else
            {
                genStartedAt ??= _env.Now;
                ReportPhase2(events, snap.Count, target, _env.Now - genStartedAt.Value);
                if (snap.NewestWriteUtc is { } w && _env.Now - w >= Settle)
                {
                    events.Report(new ProgressEvent(1.0));
                    events.Report(new StatusEvent("interop ready — game window opening…"));
                    return snap.Count;
                }
            }
            if (proc.HasExited || _env.Now - launchedAt >= Timeout) return null;
            await _env.DelayAsync(Poll, ct);
        }
    }

    private static void ReportPhase1(IProgress<LaunchEvent> events, TimeSpan waited)
    {
        events.Report(new ProgressEvent(null));
        events.Report(new StatusEvent($"First launch/update — preparing game interop (can take a few minutes)…  ({waited:m\\:ss})"));
    }

    private static void ReportPhase2(IProgress<LaunchEvent> events, int count, int target, TimeSpan elapsed)
    {
        events.Report(new ProgressEvent(Math.Min(0.99, count / (double)target)));
        events.Report(new StatusEvent($"First launch/update — generating game interop: {count}/~{target}  ({elapsed:m\\:ss})"));
    }
}

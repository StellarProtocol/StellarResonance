using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Services;

namespace StellarLauncher.Core.Launch;

public enum LaunchOutcomeKind { Started, SteamHandoff, Failed }
public sealed record LaunchOutcome(LaunchOutcomeKind Kind, int? InteropCount);

public interface ILaunchOrchestrator
{
    Task<LaunchOutcome> LaunchAsync(ClientProfile c, IProgress<LaunchEvent> events, CancellationToken ct);
}

/// <summary>The launch pipeline for ONE client (spec § 7): BepInEx cfg → DXVK-NVAPI → pre script →
/// Process.Start → interop monitor → (later) exit + post script. No static state; N clients run independently.
/// The pre-launch review dialog runs BEFORE this (App-side).</summary>
public sealed class LaunchOrchestrator : ILaunchOrchestrator
{
    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan EarlyExitGrace = TimeSpan.FromSeconds(2.5);

    private readonly IGameLauncher _launcher;
    private readonly IProcessFactory _processes;
    private readonly IBepInExConfig _bepinex;
    private readonly IDxvkNvapiInstaller _dxvk;
    private readonly InteropMonitor _interop;
    private readonly LaunchEnvironment _env;

    public LaunchOrchestrator(IGameLauncher launcher, IProcessFactory processes, IBepInExConfig bepinex,
        IDxvkNvapiInstaller dxvk, InteropMonitor interop, LaunchEnvironment env)
    {
        _launcher = launcher; _processes = processes; _bepinex = bepinex; _dxvk = dxvk; _interop = interop; _env = env;
    }

    public async Task<LaunchOutcome> LaunchAsync(ClientProfile c, IProgress<LaunchEvent> events, CancellationToken ct)
    {
        // Hoisted so the catch blocks can close an alongside pre-launch script if anything after it throws — notably
        // _processes.Start, which THROWS (Win32Exception, UseShellExecute=false) on a bad runner/exe, not returns null.
        IScriptHandle? parallel = null;
        try
        {
            _bepinex.ApplyMode(c.GameMiniDir, c.DebugLogging);
            await EnsureDxvkAsync(c, events, ct);

            var exe = GameExeResolver.StarLauncherExe(_env.Fs, c.GameMiniDir);
            var steam = _env.IsWindows ? GameExeResolver.TryGetSteamAppId(_env.Fs, c.GameMiniDir) : null;
            var psi = _launcher.BuildStartInfo(LaunchRequestBuilder.Build(c, exe, _env.UmuRun, steam, _env.MangoHudUserConfig()));

            string status;
            (status, parallel) = await RunPreScriptAsync(c, psi, events, ct);
            events.Report(new StatusEvent(status));
            var proc = _processes.Start(psi);

            // An alongside pre-launch script can't be tied to a Steam-handoff game (tracking stops here) or to a
            // launch that never started — close it now so it can't leak (it's no longer Linux-only).
            if (steam is not null) { parallel?.Kill(); parallel?.Dispose(); parallel = null; events.Report(new SteamHandoffEvent()); events.Report(new StatusEvent("launching via Steam…")); return new LaunchOutcome(LaunchOutcomeKind.SteamHandoff, null); }
            if (proc is null) { parallel?.Kill(); parallel?.Dispose(); parallel = null; events.Report(new FailedEvent("launch failed: process did not start")); return new LaunchOutcome(LaunchOutcomeKind.Failed, null); }

            events.Report(new StartedEvent(proc));
            _ = ReportExitAsync(c, proc, psi, parallel, events);
            parallel = null;   // ownership transferred to the exit waiter; the catch must not kill it now
            return await FollowStartupAsync(c, proc, events, ct);
        }
        catch (OperationCanceledException) { parallel?.Kill(); parallel?.Dispose(); throw; }
        catch (Exception ex)
        {
            parallel?.Kill(); parallel?.Dispose();
            events.Report(new FailedEvent($"launch failed: {ex.Message}"));
            return new LaunchOutcome(LaunchOutcomeKind.Failed, null);
        }
    }

    private async Task EnsureDxvkAsync(ClientProfile c, IProgress<LaunchEvent> events, CancellationToken ct)
    {
        if (_env.IsWindows || c.Linux is not { DxvkNvapi: true, WinePrefix: { } prefix } || string.IsNullOrWhiteSpace(prefix)) return;
        events.Report(new StatusEvent("checking DXVK-NVAPI…"));
        try { events.Report(new StatusEvent(await _dxvk.EnsureAsync(prefix, ct))); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { events.Report(new StatusEvent($"DXVK-NVAPI skipped: {ex.Message}")); }
    }

    // Runs the user's pre-launch script (Linux AND Windows). The script env comes from ScriptEnv, NOT
    // psi.Environment — on Windows the game psi uses UseShellExecute and even reading its Environment poisons
    // the game start. Returns the status to show and, when the script runs ALONGSIDE the game, a handle to kill
    // it on exit.
    private async Task<(string status, IScriptHandle? parallel)> RunPreScriptAsync(ClientProfile c, ProcessStartInfo psi, IProgress<LaunchEvent> events, CancellationToken ct)
    {
        var script = c.Advanced.PreLaunch;
        if (string.IsNullOrWhiteSpace(script) || !_env.Fs.File.Exists(script)) return ("launching…", null);
        var env = ScriptEnv(c, psi);
        if (!c.Advanced.PreLaunchWait)
        {
            events.Report(new StatusEvent("starting pre-launch script alongside the game…"));
            return ("launching…", _env.Scripts.Start(script, env));   // closed in ReportExitAsync when the game ends
        }
        events.Report(new StatusEvent("running pre-launch script…"));
        var code = await _env.Scripts.RunAsync(script, env, ScriptTimeout, ct);
        return (code switch
        {
            null => "pre-launch script timed out — launching anyway",
            0 => "launching…",
            _ => $"pre-launch script exited {code} — launching anyway",
        }, null);
    }

    // The environment handed to a user pre/post script. On Linux it is the game's fully-computed launch env
    // (WINEPREFIX, dll overrides, …) — safe to read on the UseShellExecute=false psi. On Windows the game psi
    // uses UseShellExecute, so even READING psi.Environment initializes its backing dict and makes the game's
    // Process.Start throw; the script instead gets the user's own advanced env vars (it already inherits the
    // launcher's process environment).
    private IEnumerable<KeyValuePair<string, string?>> ScriptEnv(ClientProfile c, ProcessStartInfo psi)
        => _env.IsWindows
            ? c.Advanced.Env.Where(e => !string.IsNullOrEmpty(e.Name))
                            .Select(e => new KeyValuePair<string, string?>(e.Name, (string?)e.Value))
            : psi.Environment;

    private async Task<LaunchOutcome> FollowStartupAsync(ClientProfile c, IGameProcess proc, IProgress<LaunchEvent> events, CancellationToken ct)
    {
        int? count = null;
        if (_interop.RegenExpected(c.GameMiniDir))
        {
            events.Report(new PreparingEvent());
            count = await _interop.RunAsync(c.GameMiniDir, proc, c.LastInteropCount, events, ct);
        }
        else
        {
            await _env.DelayAsync(EarlyExitGrace, ct);
        }
        if (proc.HasExited) return new LaunchOutcome(proc.ExitCode == 0 ? LaunchOutcomeKind.Started : LaunchOutcomeKind.Failed, count);
        events.Report(new RunningEvent());
        events.Report(new StatusEvent("game running"));
        return new LaunchOutcome(LaunchOutcomeKind.Started, count);
    }

    // Fire-and-forget: close the alongside pre-launch script, then report the exit and run the post-exit script (Linux).
    private async Task ReportExitAsync(ClientProfile c, IGameProcess proc, ProcessStartInfo psi, IScriptHandle? parallel, IProgress<LaunchEvent> events)
    {
        int exitCode = -1;
        try
        {
            await proc.WaitForExitAsync(CancellationToken.None);
            exitCode = proc.ExitCode;
            if (parallel is not null) { try { parallel.Kill(); parallel.Dispose(); } catch { /* best effort */ } }   // "closed when the game closed"
            var post = c.Advanced.PostExit;
            if (!string.IsNullOrWhiteSpace(post) && _env.Fs.File.Exists(post))
                await _env.Scripts.RunAsync(post, ScriptEnv(c, psi), ScriptTimeout);
        }
        catch (Exception ex) { events.Report(new StatusEvent($"post-exit script failed: {ex.Message}")); }
        // Always report the exit so the session leaves Running even if the post-exit script (or kill) threw.
        events.Report(new ExitedEvent(exitCode));
        if (exitCode != 0) events.Report(new StatusEvent($"exited with code {exitCode} — check Runner / WINEPREFIX in Settings"));
    }
}

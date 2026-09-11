using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Launch;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;
using Xunit;

public class LaunchOrchestratorTests
{
    private const string G = "/opt/game/P/drive_c/Star/StarLauncher/game/release_3.7/game_mini";

    private sealed class Platform(bool win) : IPlatformInfo { public bool IsWindows => win; public string AppDataDir => "/cfg"; }
    private sealed class Detector : IGameDetector
    {
        public IReadOnlyList<string> Detect() => Array.Empty<string>();
        public string? DetectRunner() => null;
        public string? DetectUmu() => "/usr/bin/umu-run";
    }
    private sealed class Launcher : IGameLauncher
    {
        public LaunchRequest? Last;
        public ProcessStartInfo BuildStartInfo(LaunchRequest r) { Last = r; return new ProcessStartInfo { FileName = r.Runner ?? r.StarLauncherExe, UseShellExecute = false }; }
        public Process? Launch(LaunchRequest r) => null;
    }
    private sealed class Factory(IGameProcess? proc) : IProcessFactory
    {
        public int Starts;
        public IGameProcess? Start(ProcessStartInfo psi) { Starts++; return proc; }
        public IGameProcess? Attach(int pid) => null;
    }
    private sealed class BepInEx : IBepInExConfig { public (string, bool)? Applied; public void ApplyMode(string d, bool debug) => Applied = (d, debug); }
    private sealed class Dxvk : IDxvkNvapiInstaller { public int Calls; public Task<string> EnsureAsync(string p, CancellationToken ct = default) { Calls++; return Task.FromResult("DXVK-NVAPI up to date"); } }
    private sealed class Watch(bool regen, Queue<InteropSnapshot> snaps) : IInteropWatch
    {
        public bool RegenExpected(string d) => regen;
        public InteropSnapshot Snapshot(string d) => snaps.Count > 1 ? snaps.Dequeue() : snaps.Peek();
    }
    private sealed class Sink : IProgress<LaunchEvent>
    {
        public readonly List<LaunchEvent> Events = new();
        public void Report(LaunchEvent e) => Events.Add(e);
    }

    private static readonly DateTimeOffset T0 = new(2026, 9, 11, 2, 31, 0, TimeSpan.Zero);

    private static (LaunchOrchestrator sut, Launcher launcher, BepInEx bep, Dxvk dxvk, MockFileSystem fs) Build(
        IGameProcess? proc, bool windows = false, bool regen = false, Queue<InteropSnapshot>? snaps = null)
    {
        var fs = new MockFileSystem();
        fs.AddFile("/opt/game/P/drive_c/Star/StarLauncher/StarLauncher.exe", new MockFileData("mz"));
        fs.AddDirectory(G);
        var clock = T0;
        var env = new LaunchEnvironment(fs, new Platform(windows), new Detector(),
            now: () => clock, delay: (d, ct) => { clock += d; return Task.CompletedTask; }) { MangoHudUserConfig = () => false };
        var launcher = new Launcher(); var bep = new BepInEx(); var dxvk = new Dxvk();
        var watch = new Watch(regen, snaps ?? new Queue<InteropSnapshot>(new[] { new InteropSnapshot(0, null) }));
        var sut = new LaunchOrchestrator(launcher, new Factory(proc), bep, dxvk, new InteropMonitor(watch, env), env);
        return (sut, launcher, bep, dxvk, fs);
    }

    private static ClientProfile Linux() => new()
    {
        Id = "c1", GameMiniDir = G, DebugLogging = true, LastInteropCount = 190,
        Linux = new LinuxRuntime { Runner = "/p/proton", WinePrefix = "/opt/game/P", DxvkNvapi = true },
    };

    [Fact]
    public async Task Linux_launch_without_regen_reports_started_then_running()
    {
        var proc = new LaunchSessionTests.FakeProcess();
        var (sut, launcher, bep, dxvk, _) = Build(proc);
        var sink = new Sink();

        var outcome = await sut.LaunchAsync(Linux(), sink, CancellationToken.None);

        Assert.Equal(LaunchOutcomeKind.Started, outcome.Kind);
        Assert.Null(outcome.InteropCount);
        Assert.Equal((G, true), bep.Applied);
        Assert.Equal(1, dxvk.Calls);
        Assert.Equal("/usr/bin/umu-run", launcher.Last!.UmuRun);
        Assert.Equal("/opt/game/P/drive_c/Star/StarLauncher/StarLauncher.exe", launcher.Last.StarLauncherExe);
        Assert.Contains(sink.Events, e => e is StartedEvent s && ReferenceEquals(s.Process, proc));
        Assert.Contains(sink.Events, e => e is RunningEvent);
        Assert.Equal("game running", ((StatusEvent)sink.Events[^1]).Text);
        Assert.DoesNotContain(sink.Events, e => e is PreparingEvent);
    }

    [Fact]
    public async Task Regen_runs_the_interop_monitor_and_returns_the_settled_count()
    {
        var proc = new LaunchSessionTests.FakeProcess();
        var written = T0.AddSeconds(30);
        var snaps = new Queue<InteropSnapshot>(new[]
        {
            new InteropSnapshot(0, null),                 // phase 1: nothing written yet
            new InteropSnapshot(120, written),            // phase 2: assemblies landing
            new InteropSnapshot(193, written.AddSeconds(2)),
            new InteropSnapshot(193, written.AddSeconds(2)),  // repeats until the 10 s settle passes
        });
        var (sut, _, _, _, _) = Build(proc, regen: true, snaps: snaps);
        var sink = new Sink();

        var outcome = await sut.LaunchAsync(Linux(), sink, CancellationToken.None);

        Assert.Equal(LaunchOutcomeKind.Started, outcome.Kind);
        Assert.Equal(193, outcome.InteropCount);
        Assert.Contains(sink.Events, e => e is PreparingEvent);
        Assert.Contains(sink.Events, e => e is ProgressEvent { Fraction: null });
        Assert.Contains(sink.Events, e => e is ProgressEvent { Fraction: > 0.6 and < 0.7 });   // 120/190
        Assert.Contains(sink.Events, e => e is StatusEvent { Text: "interop ready — game window opening…" });
        Assert.Contains(sink.Events, e => e is RunningEvent);
    }

    [Fact]
    public async Task Process_that_fails_to_start_reports_Failed()
    {
        var (sut, _, _, _, _) = Build(proc: null);
        var sink = new Sink();
        var outcome = await sut.LaunchAsync(Linux(), sink, CancellationToken.None);
        Assert.Equal(LaunchOutcomeKind.Failed, outcome.Kind);
        Assert.Contains(sink.Events, e => e is FailedEvent f && f.Message.Contains("did not start"));
    }

    [Fact]
    public async Task Windows_steam_install_hands_off_and_owns_no_process()
    {
        var fs = new MockFileSystem();
        const string steam = "/games/SteamLibrary/steamapps/common/Blue Protocol Star Resonance";
        fs.AddFile("/games/SteamLibrary/steamapps/appmanifest_2358720.acf", new MockFileData("\"AppState\"\n{\n\t\"appid\"\t\t\"2358720\"\n\t\"installdir\"\t\t\"Blue Protocol Star Resonance\"\n}"));
        fs.AddDirectory(steam + "/StarSEA_STEAM_Data");
        fs.AddFile(steam + "/StarSEA_STEAM.exe", new MockFileData("mz"));
        var env = new LaunchEnvironment(fs, new Platform(true), new Detector(), () => T0, (d, ct) => Task.CompletedTask);
        var launcher = new Launcher();
        var factory = new Factory(new LaunchSessionTests.FakeProcess());
        var sut = new LaunchOrchestrator(launcher, factory, new BepInEx(), new Dxvk(),
            new InteropMonitor(new Watch(false, new Queue<InteropSnapshot>(new[] { new InteropSnapshot(0, null) })), env), env);
        var sink = new Sink();

        var outcome = await sut.LaunchAsync(new ClientProfile { Id = "w", GameMiniDir = steam }, sink, CancellationToken.None);

        Assert.Equal(LaunchOutcomeKind.SteamHandoff, outcome.Kind);
        Assert.Equal(1, factory.Starts);
        Assert.Equal("2358720", launcher.Last!.SteamAppId);
        Assert.Contains(sink.Events, e => e is SteamHandoffEvent);
        Assert.DoesNotContain(sink.Events, e => e is StartedEvent);
    }

    [Fact]
    public async Task Exit_is_reported_after_the_process_ends()
    {
        var proc = new LaunchSessionTests.FakeProcess();
        var (sut, _, _, _, _) = Build(proc);
        var sink = new Sink();
        await sut.LaunchAsync(Linux(), sink, CancellationToken.None);

        proc.Exit(0);
        for (var i = 0; i < 100 && !sink.Events.Any(e => e is ExitedEvent); i++) await Task.Delay(10);

        Assert.Contains(sink.Events, e => e is ExitedEvent { ExitCode: 0 });
    }

    // ---- pre-launch script: wait vs. run-alongside (owner 2026-09-11) ----
    private sealed class FakeScripts : IScriptRunner
    {
        public int RunCalls, StartCalls;
        public readonly FakeHandle Handle = new();
        public Task<int?> RunAsync(string p, IEnumerable<KeyValuePair<string, string?>> env, TimeSpan t, CancellationToken ct = default) { RunCalls++; return Task.FromResult<int?>(0); }
        public IScriptHandle? Start(string p, IEnumerable<KeyValuePair<string, string?>> env) { StartCalls++; return Handle; }
    }
    private sealed class FakeHandle : IScriptHandle
    {
        public bool Killed, Disposed;
        public bool HasExited => Killed;
        public void Kill() => Killed = true;
        public void Dispose() => Disposed = true;
    }

    private static (LaunchOrchestrator sut, FakeScripts scripts, LaunchSessionTests.FakeProcess proc) BuildWithScripts()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/opt/game/P/drive_c/Star/StarLauncher/StarLauncher.exe", new MockFileData("mz"));
        fs.AddDirectory(G);
        fs.AddFile("/opt/game/P/pre.sh", new MockFileData("echo hi\n"));
        var scripts = new FakeScripts();
        var clock = T0;
        var env = new LaunchEnvironment(fs, new Platform(false), new Detector(),
            now: () => clock, delay: (d, ct) => { clock += d; return Task.CompletedTask; }) { MangoHudUserConfig = () => false, Scripts = scripts };
        var proc = new LaunchSessionTests.FakeProcess();
        var watch = new Watch(false, new Queue<InteropSnapshot>(new[] { new InteropSnapshot(0, null) }));
        var sut = new LaunchOrchestrator(new Launcher(), new Factory(proc), new BepInEx(), new Dxvk(), new InteropMonitor(watch, env), env);
        return (sut, scripts, proc);
    }

    private static ClientProfile LinuxWithPre(bool wait) => new()
    {
        Id = "c1", GameMiniDir = G, LastInteropCount = 190,
        Linux = new LinuxRuntime { Runner = "/p/proton", WinePrefix = "/opt/game/P", DxvkNvapi = false },
        Advanced = new AdvancedOptions { PreLaunch = "/opt/game/P/pre.sh", PreLaunchWait = wait },
    };

    [Fact]
    public async Task Pre_launch_script_waits_by_default()
    {
        var (sut, scripts, _) = BuildWithScripts();
        await sut.LaunchAsync(LinuxWithPre(wait: true), new Sink(), CancellationToken.None);
        Assert.Equal(1, scripts.RunCalls);
        Assert.Equal(0, scripts.StartCalls);
    }

    private sealed class ThrowingFactory : IProcessFactory
    {
        public IGameProcess? Start(ProcessStartInfo psi) => throw new System.ComponentModel.Win32Exception("No such file or directory");
        public IGameProcess? Attach(int pid) => null;
    }

    [Fact]
    public async Task Parallel_pre_launch_script_is_killed_when_the_process_fails_to_START_by_throwing()
    {
        // SystemProcessFactory.Start THROWS (not returns null) on a bad runner/exe — the alongside script must not leak.
        var fs = new MockFileSystem();
        fs.AddFile("/opt/game/P/drive_c/Star/StarLauncher/StarLauncher.exe", new MockFileData("mz"));
        fs.AddDirectory(G);
        fs.AddFile("/opt/game/P/pre.sh", new MockFileData("echo hi\n"));
        var scripts = new FakeScripts();
        var clock = T0;
        var env = new LaunchEnvironment(fs, new Platform(false), new Detector(),
            now: () => clock, delay: (d, ct) => { clock += d; return Task.CompletedTask; }) { MangoHudUserConfig = () => false, Scripts = scripts };
        var sut = new LaunchOrchestrator(new Launcher(), new ThrowingFactory(), new BepInEx(), new Dxvk(),
            new InteropMonitor(new Watch(false, new Queue<InteropSnapshot>(new[] { new InteropSnapshot(0, null) })), env), env);

        var outcome = await sut.LaunchAsync(LinuxWithPre(wait: false), new Sink(), CancellationToken.None);

        Assert.Equal(LaunchOutcomeKind.Failed, outcome.Kind);
        Assert.Equal(1, scripts.StartCalls);
        Assert.True(scripts.Handle.Killed);          // not leaked
        Assert.True(scripts.Handle.Disposed);
    }

    [Fact]
    public async Task Pre_launch_parallel_starts_alongside_and_is_killed_when_the_game_exits()
    {
        var (sut, scripts, proc) = BuildWithScripts();
        var sink = new Sink();
        await sut.LaunchAsync(LinuxWithPre(wait: false), sink, CancellationToken.None);

        Assert.Equal(1, scripts.StartCalls);        // started alongside
        Assert.Equal(0, scripts.RunCalls);          // not waited on
        Assert.False(scripts.Handle.Killed);        // still running while the game runs

        proc.Exit(0);
        for (var i = 0; i < 100 && !scripts.Handle.Killed; i++) await Task.Delay(10);
        Assert.True(scripts.Handle.Killed);          // closed when the game closed
        Assert.True(scripts.Handle.Disposed);
    }
}

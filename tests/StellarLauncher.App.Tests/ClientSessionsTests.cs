using System.Diagnostics;
using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.App.Services;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Launch;
using StellarLauncher.Core.Platform;
using Xunit;

public class ClientSessionsTests
{
    private sealed class Platform : IPlatformInfo { public bool IsWindows => false; public string AppDataDir => "/cfg"; }

    private sealed class FakeOrchestrator : ILaunchOrchestrator
    {
        public readonly List<string> Launched = new();
        public LaunchOutcome Outcome = new(LaunchOutcomeKind.Started, 193);
        public Task<LaunchOutcome> LaunchAsync(ClientProfile c, IProgress<LaunchEvent> ev, CancellationToken ct)
        {
            Launched.Add(c.Id);
            ev.Report(new StartedEvent(new FakeProcess()));
            ev.Report(new RunningEvent());
            return Task.FromResult(Outcome);
        }
    }
    private sealed class FakeProcess : IGameProcess
    {
        public int Id => 1; public bool HasExited => false; public int ExitCode => 0; public bool Killed;
        public Task WaitForExitAsync(CancellationToken ct) => new TaskCompletionSource().Task;
        public void Kill(bool tree) => Killed = true;
    }
    private sealed class Review(bool answer) : IPreLaunchReview
    {
        public int Calls;
        public Task<bool> ReviewAsync(ClientProfile c, CancellationToken ct) { Calls++; return Task.FromResult(answer); }
    }
    private sealed class Scanner(params RunningProcess[] procs) : IRunningProcessScanner
    {
        public IReadOnlyList<RunningProcess> Snapshot() => procs;
    }
    private sealed class Factory : IProcessFactory
    {
        public IGameProcess? Start(ProcessStartInfo psi) => null;
        public IGameProcess? Attach(int pid) => new FakeProcess();
    }

    private static readonly DateTimeOffset T0 = new(2026, 9, 11, 3, 0, 0, TimeSpan.Zero);

    private static (ClientSessions sut, ConfigStore store, FakeOrchestrator orch, ClientProfile c) Build(params RunningProcess[] running)
    {
        var fs = new MockFileSystem();
        var store = new ConfigStore(fs, new Platform());
        var cfg = new LauncherConfig();
        var c = new ClientProfile { Id = "c1", Name = "Main", GameMiniDir = "/opt/game/P/drive_c/Star/StarLauncher/game/release_3.7/game_mini", LastInteropCount = 0 };
        cfg.Clients.Add(c);
        store.Save(cfg);
        var orch = new FakeOrchestrator();
        var sut = new ClientSessions(store, orch, new Scanner(running), new Factory(), () => T0, run => run());
        return (sut, store, orch, c);
    }

    [Fact]
    public async Task Launch_runs_review_then_orchestrator_and_persists_interop_count()
    {
        var (sut, store, orch, c) = Build();
        var review = new Review(true);
        var changes = 0; sut.SessionChanged += _ => changes++;

        await sut.LaunchAsync(c, review, CancellationToken.None);

        Assert.Equal(1, review.Calls);
        Assert.Equal(new[] { "c1" }, orch.Launched);
        var s = sut.For(c);
        Assert.Equal(SessionState.Running, s.State);
        Assert.Equal(T0, s.StartedAt);
        Assert.Equal(193, store.Load().Clients[0].LastInteropCount);
        Assert.True(changes >= 3);
    }

    [Fact]
    public async Task Cancelled_review_launches_nothing()
    {
        var (sut, _, orch, c) = Build();
        await sut.LaunchAsync(c, new Review(false), CancellationToken.None);
        Assert.Empty(orch.Launched);
        Assert.Equal(SessionState.Idle, sut.For(c).State);
    }

    [Fact]
    public async Task Busy_session_ignores_a_second_launch_and_Stop_kills_it()
    {
        var (sut, _, orch, c) = Build();
        await sut.LaunchAsync(c, new Review(true), CancellationToken.None);
        await sut.LaunchAsync(c, new Review(true), CancellationToken.None);
        Assert.Single(orch.Launched);

        var p = (FakeProcess)sut.For(c).Process!;
        sut.Stop(c);
        Assert.True(p.Killed);
    }

    [Fact]
    public void Reattach_marks_a_client_running_when_its_process_is_found()
    {
        var (sut, _, _, c) = Build(new RunningProcess(4242, @"Z:\opt\game\P\drive_c\Star\StarLauncher\StarLauncher.exe"));
        sut.Reattach(new[] { c });
        Assert.Equal(SessionState.Running, sut.For(c).State);
        Assert.True(sut.For(c).CanStop);
    }

    [Fact]
    public void Forget_drops_the_session()
    {
        var (sut, _, _, c) = Build();
        _ = sut.For(c);
        sut.Forget("c1");
        Assert.Null(sut.TryGet("c1"));
    }

    [Fact]
    public async Task Launch_cancelled_sets_failed_state_and_recovers_canlaunch()
    {
        var cancellingOrch = new CancellingOrchestrator();
        var fs = new MockFileSystem();
        var store = new ConfigStore(fs, new Platform());
        var cfg = new LauncherConfig();
        var c = new ClientProfile { Id = "c1", Name = "Main", GameMiniDir = "/opt/game/P/drive_c/Star/StarLauncher/game/release_3.7/game_mini", LastInteropCount = 0 };
        cfg.Clients.Add(c);
        store.Save(cfg);
        var sut = new ClientSessions(store, cancellingOrch, new Scanner(), new Factory(), () => T0, run => run());

        await sut.LaunchAsync(c, new Review(true), CancellationToken.None);

        var s = sut.For(c);
        Assert.False(s.IsBusy);
        Assert.True(s.CanLaunch);
    }

    [Fact]
    public async Task Launch_exception_sets_failed_state_and_recovers_canlaunch()
    {
        var failingOrch = new FailingOrchestrator();
        var fs = new MockFileSystem();
        var store = new ConfigStore(fs, new Platform());
        var cfg = new LauncherConfig();
        var c = new ClientProfile { Id = "c1", Name = "Main", GameMiniDir = "/opt/game/P/drive_c/Star/StarLauncher/game/release_3.7/game_mini", LastInteropCount = 0 };
        cfg.Clients.Add(c);
        store.Save(cfg);
        var sut = new ClientSessions(store, failingOrch, new Scanner(), new Factory(), () => T0, run => run());

        await sut.LaunchAsync(c, new Review(true), CancellationToken.None);

        var s = sut.For(c);
        Assert.Equal(SessionState.Failed, s.State);
        Assert.True(s.CanLaunch);
    }

    [Fact]
    public async Task Second_launch_during_review_is_ignored()
    {
        var fs = new MockFileSystem();
        var store = new ConfigStore(fs, new Platform());
        var cfg = new LauncherConfig();
        var c = new ClientProfile { Id = "c1", Name = "Main", GameMiniDir = "/opt/game/P/drive_c/Star/StarLauncher/game/release_3.7/game_mini", LastInteropCount = 0 };
        cfg.Clients.Add(c);
        store.Save(cfg);
        var orch = new FakeOrchestrator();
        var sut = new ClientSessions(store, orch, new Scanner(), new Factory(), () => T0, run => run());

        // Review TCS to control the review timing
        var reviewTcs = new TaskCompletionSource<bool>();
        var delayedReview = new DelayedReview(reviewTcs);

        // Start two launches without awaiting
        var task1 = sut.LaunchAsync(c, delayedReview, CancellationToken.None);
        var task2 = sut.LaunchAsync(c, delayedReview, CancellationToken.None);

        // Verify review was invoked only once (second launch blocked by _pending)
        Assert.Equal(1, delayedReview.Calls);

        // Complete the review to allow first launch to proceed
        reviewTcs.SetResult(true);
        await task1;
        await task2;

        // Orchestrator should have launched exactly once
        Assert.Single(orch.Launched);
    }

    private sealed class DelayedReview(TaskCompletionSource<bool> tcs) : IPreLaunchReview
    {
        public int Calls;
        public Task<bool> ReviewAsync(ClientProfile c, CancellationToken ct) { Calls++; return tcs.Task; }
    }

    private sealed class CancellingOrchestrator : ILaunchOrchestrator
    {
        public Task<LaunchOutcome> LaunchAsync(ClientProfile c, IProgress<LaunchEvent> ev, CancellationToken ct)
            => throw new OperationCanceledException("cancelled");
    }

    private sealed class FailingOrchestrator : ILaunchOrchestrator
    {
        public Task<LaunchOutcome> LaunchAsync(ClientProfile c, IProgress<LaunchEvent> ev, CancellationToken ct)
            => throw new InvalidOperationException("something went wrong");
    }

    private sealed class DelayedOrchestrator(TaskCompletionSource<LaunchOutcome> tcs) : ILaunchOrchestrator
    {
        public int LaunchCount;
        public async Task<LaunchOutcome> LaunchAsync(ClientProfile c, IProgress<LaunchEvent> ev, CancellationToken ct)
        {
            LaunchCount++;
            return await tcs.Task;
        }
    }
}

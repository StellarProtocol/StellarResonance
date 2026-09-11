using System;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Launch;
using Xunit;

public class LaunchSessionTests
{
    public sealed class FakeProcess : IGameProcess
    {
        public int Id { get; init; } = 4242;
        public bool HasExited { get; set; }
        public int ExitCode { get; set; }
        public bool Killed { get; private set; }
        private readonly TaskCompletionSource _exit = new();
        public Task WaitForExitAsync(CancellationToken ct) => _exit.Task.WaitAsync(ct);
        public void Kill(bool entireProcessTree) { Killed = true; Exit(137); }
        public void Exit(int code) { ExitCode = code; HasExited = true; _exit.TrySetResult(); }
    }

    private static readonly DateTimeOffset T0 = new(2026, 9, 11, 2, 31, 0, TimeSpan.Zero);

    [Fact]
    public void Happy_path_idle_to_running_to_exited()
    {
        var s = new LaunchSession("c1");
        var changes = 0; s.Changed += _ => changes++;
        var p = new FakeProcess();

        Assert.Equal(SessionState.Idle, s.State);
        Assert.True(s.CanLaunch); Assert.False(s.CanStop);

        s.Begin(T0);
        Assert.Equal(SessionState.Launching, s.State); Assert.Equal(T0, s.StartedAt); Assert.False(s.CanLaunch);

        s.Apply(new StatusEvent("launching…"));
        s.Apply(new StartedEvent(p));
        Assert.True(s.CanStop);
        s.Apply(new PreparingEvent());
        s.Apply(new ProgressEvent(null));
        Assert.True(s.ProgressIndeterminate);
        s.Apply(new ProgressEvent(0.75));
        Assert.Equal(0.75, s.Progress); Assert.False(s.ProgressIndeterminate);
        s.Apply(new RunningEvent());
        Assert.Equal(SessionState.Running, s.State); Assert.Null(s.Progress);

        s.Apply(new ExitedEvent(0));
        Assert.Equal(SessionState.Exited, s.State); Assert.Equal(0, s.ExitCode); Assert.Null(s.Process);
        Assert.True(s.CanLaunch);
        Assert.True(changes >= 8);
    }

    [Fact]
    public void Nonzero_exit_and_failure_land_in_Failed_and_can_relaunch()
    {
        var s = new LaunchSession("c1");
        s.Begin(T0); s.Apply(new StartedEvent(new FakeProcess())); s.Apply(new RunningEvent());
        s.Apply(new ExitedEvent(1));
        Assert.Equal(SessionState.Failed, s.State); Assert.Equal(1, s.ExitCode); Assert.True(s.CanLaunch);

        s.Begin(T0.AddMinutes(1));
        s.Apply(new FailedEvent("launch failed: runner missing"));
        Assert.Equal(SessionState.Failed, s.State);
        Assert.Equal("launch failed: runner missing", s.StatusText);
        Assert.Null(s.ExitCode);
    }

    [Fact]
    public void Begin_while_busy_throws()
    {
        var s = new LaunchSession("c1");
        s.Begin(T0); s.Apply(new StartedEvent(new FakeProcess())); s.Apply(new RunningEvent());
        Assert.Throws<InvalidOperationException>(() => s.Begin(T0));
    }

    [Fact]
    public void Stop_kills_the_tree_only_when_a_process_is_owned()
    {
        var s = new LaunchSession("c1");
        s.Stop();   // idle: no-op, no throw
        var p = new FakeProcess();
        s.Begin(T0); s.Apply(new StartedEvent(p)); s.Apply(new RunningEvent());
        s.Stop();
        Assert.True(p.Killed);
    }

    [Fact]
    public void Steam_handoff_owns_no_process_and_stays_launchable()
    {
        var s = new LaunchSession("c1");
        s.Begin(T0); s.Apply(new SteamHandoffEvent());
        Assert.Equal(SessionState.SteamHandoff, s.State); Assert.Null(s.Process);
        Assert.False(s.CanStop); Assert.True(s.CanLaunch);
    }

    [Fact]
    public void AttachRunning_enters_Running_directly()
    {
        var s = new LaunchSession("c1");
        s.AttachRunning(new FakeProcess(), T0);
        Assert.Equal(SessionState.Running, s.State); Assert.True(s.CanStop); Assert.Equal(T0, s.StartedAt);
    }
}

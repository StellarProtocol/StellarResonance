using System;

namespace StellarLauncher.Core.Launch;

public enum SessionState { Idle, Launching, Preparing, Running, SteamHandoff, Exited, Failed }

/// <summary>One client's live launch state (spec § 7). Tiles, rail rows and the workspace header bind here.</summary>
public sealed class LaunchSession
{
    public string ClientId { get; }
    public SessionState State { get; private set; } = SessionState.Idle;
    public string StatusText { get; private set; } = "";
    public double? Progress { get; private set; }
    public bool ProgressIndeterminate { get; private set; }
    public IGameProcess? Process { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public int? ExitCode { get; private set; }

    public event Action<LaunchSession>? Changed;

    public LaunchSession(string clientId) => ClientId = clientId;

    public bool IsBusy => State is SessionState.Launching or SessionState.Preparing or SessionState.Running;
    public bool CanLaunch => !IsBusy;
    public bool CanStop => Process is not null && IsBusy;

    public void Begin(DateTimeOffset now)
    {
        if (!CanLaunch) throw new InvalidOperationException($"cannot launch while {State}");
        State = SessionState.Launching; StartedAt = now; ExitCode = null; Process = null;
        Progress = null; ProgressIndeterminate = false; StatusText = "launching…";
        Raise();
    }

    public void Apply(LaunchEvent e)
    {
        switch (e)
        {
            case StatusEvent s: StatusText = s.Text; break;
            case ProgressEvent p: Progress = p.Fraction; ProgressIndeterminate = p.Fraction is null; break;
            case StartedEvent st: Process = st.Process; break;
            case PreparingEvent: State = SessionState.Preparing; break;
            case RunningEvent: State = SessionState.Running; Progress = null; ProgressIndeterminate = false; break;
            case SteamHandoffEvent: State = SessionState.SteamHandoff; Process = null; break;
            case ExitedEvent x:
                State = x.ExitCode == 0 ? SessionState.Exited : SessionState.Failed;
                ExitCode = x.ExitCode; Process = null; Progress = null; ProgressIndeterminate = false; break;
            case FailedEvent f:
                State = SessionState.Failed; StatusText = f.Message; Progress = null; ProgressIndeterminate = false; break;
        }
        Raise();
    }

    /// <summary>Kill the owned process tree; the orchestrator's exit waiter then reports <see cref="ExitedEvent"/>.</summary>
    public void Stop()
    {
        if (!CanStop) return;
        Process!.Kill(entireProcessTree: true);
    }

    public void AttachRunning(IGameProcess process, DateTimeOffset now)
    {
        Process = process; State = SessionState.Running; StartedAt = now; ExitCode = null;
        StatusText = "game running (re-attached)";
        Raise();
    }

    private void Raise() => Changed?.Invoke(this);
}

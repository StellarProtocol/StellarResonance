using System;
using StellarLauncher.Core.Localization;

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
    /// <summary>What the pre-launch review is doing right now (e.g. "Preparing &lt;plugin&gt;: …"), shown on
    /// the state line before <see cref="Begin"/>; null when no review is running. Kept apart from
    /// <see cref="StatusText"/> so a previous run's failure message survives a cancelled review.</summary>
    public string? ReviewText { get; private set; }

    /// <summary>R-2: the last "Added &lt;dependency&gt; for &lt;plugin&gt;" notice from a pre-launch review.
    /// Unlike <see cref="ReviewText"/> (live, in-progress feedback that <see cref="Begin"/> always clears,
    /// along with the caller's own <c>finally</c> once the review ends), this is STICKY — it survives
    /// <see cref="Begin"/> and the whole launch (through Running and exit), so the player actually gets to
    /// see it, until the caller clears it at the start of the NEXT launch attempt or it is explicitly
    /// dismissed via <see cref="ClearDependencyNotice"/>.</summary>
    public string? DependencyNotice { get; private set; }

    public event Action<LaunchSession>? Changed;

    public LaunchSession(string clientId) => ClientId = clientId;

    public bool IsBusy => State is SessionState.Launching or SessionState.Preparing or SessionState.Running;
    public bool CanLaunch => !IsBusy;
    public bool CanStop => Process is not null && IsBusy;

    public void Begin(DateTimeOffset now)
    {
        if (!CanLaunch) throw new InvalidOperationException($"cannot launch while {State}");
        State = SessionState.Launching; StartedAt = now; ExitCode = null; Process = null;
        Progress = null; ProgressIndeterminate = false; StatusText = L.T("launch.launching"); ReviewText = null;
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

    /// <summary>Sets (or, with null, clears) <see cref="ReviewText"/>. Ignored while the session is busy —
    /// a running game's state line is never overwritten by a review.</summary>
    public void SetReviewText(string? text)
    {
        if (IsBusy || ReviewText == text) return;
        ReviewText = text;
        Raise();
    }

    /// <summary>R-2: sets the sticky dependency notice (never ignored while busy — unlike
    /// <see cref="SetReviewText"/>, the caller RAISES this one specifically so it survives the launch that's
    /// about to start).</summary>
    public void SetDependencyNotice(string text)
    {
        if (DependencyNotice == text) return;
        DependencyNotice = text;
        Raise();
    }

    /// <summary>R-2: clears the sticky dependency notice — called by the caller at the start of the next
    /// launch attempt ("until the next launch"), or by an explicit player dismissal ("until dismissed").</summary>
    public void ClearDependencyNotice()
    {
        if (DependencyNotice is null) return;
        DependencyNotice = null;
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
        StatusText = L.T("launch.reattached");
        Raise();
    }

    private void Raise() => Changed?.Invoke(this);
}

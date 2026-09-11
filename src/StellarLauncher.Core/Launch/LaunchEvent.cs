namespace StellarLauncher.Core.Launch;

/// <summary>Typed progress from <c>LaunchOrchestrator</c> to a <c>LaunchSession</c>.</summary>
public abstract record LaunchEvent;
public sealed record StatusEvent(string Text) : LaunchEvent;
/// <param name="Fraction">0..1, or null for an indeterminate bar.</param>
public sealed record ProgressEvent(double? Fraction) : LaunchEvent;
public sealed record StartedEvent(IGameProcess Process) : LaunchEvent;
public sealed record PreparingEvent : LaunchEvent;
public sealed record RunningEvent : LaunchEvent;
public sealed record SteamHandoffEvent : LaunchEvent;
public sealed record ExitedEvent(int ExitCode) : LaunchEvent;
public sealed record FailedEvent(string Message) : LaunchEvent;

using System;
using Avalonia.Media;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Launch;

namespace StellarLauncher.App.Services;

/// <summary>One place that turns a session + inventory into the state line and state colour (tile and workspace header).</summary>
public static class SessionPresenter
{
    public static string StateLine(LaunchSession s, InventorySnapshot inv, bool modded) => s.State switch
    {
        _ when !s.IsBusy && s.ReviewText is { Length: > 0 } review => review,   // pre-launch review (e.g. dependencies)
        SessionState.Running when s.StartedAt is { } t => $"running · {Elapsed(t)}",
        SessionState.Running => "running",
        SessionState.Launching or SessionState.Preparing => s.StatusText,
        SessionState.SteamHandoff => "launched via Steam",
        SessionState.Failed => s.StatusText.Length > 0 ? s.StatusText : "failed",
        // N-1: the sticky "Added <dependency> for <plugin>" notice — checked AFTER Failed/SteamHandoff
        // (neither is in IsBusy, so without this ordering the notice would hide a launch failure's reason,
        // or Steam's "launched via Steam") — but still BEFORE the plain Exited/idle fallbacks below, which
        // is exactly where a notice is meant to show: once the launch has settled with nothing more urgent
        // to report. Never cleared by Begin/the review's own end, so it's actually visible.
        _ when !s.IsBusy && s.DependencyNotice is { Length: > 0 } notice => notice,
        SessionState.Exited => "exited",
        _ => !inv.FolderExists ? "folder missing" : modded ? "idle" : "idle · launches vanilla until the framework is installed",
    };

    public static IBrush StateBrush(LaunchSession s, InventorySnapshot inv) => new SolidColorBrush(Color.Parse(s.State switch
    {
        _ when !s.IsBusy && s.ReviewText is { Length: > 0 } => "#9ec2ff",   // same colour as Launching/Preparing
        SessionState.Running or SessionState.SteamHandoff => "#54e3a0",
        SessionState.Launching or SessionState.Preparing => "#9ec2ff",
        SessionState.Failed => "#ff9a9a",
        // N-1: moved below Failed/SteamHandoff — same reasoning as StateLine above.
        _ when !s.IsBusy && s.DependencyNotice is { Length: > 0 } => "#9ec2ff",   // R-2: neutral info colour
        _ => inv.FolderExists ? "#697297" : "#ff9a9a",
    }));

    public static string Elapsed(DateTimeOffset since)
    {
        var d = DateTimeOffset.UtcNow - since;
        return d.TotalHours >= 1 ? $"{(int)d.TotalHours} h {d.Minutes} min" : $"{Math.Max(0, (int)d.TotalMinutes)} min";
    }
}

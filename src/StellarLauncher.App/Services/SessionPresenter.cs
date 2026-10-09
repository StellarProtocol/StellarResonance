using System;
using Avalonia.Media;
using StellarLauncher.App.Localization;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Launch;

namespace StellarLauncher.App.Services;

/// <summary>One place that turns a session + inventory into the state line and state colour (tile and workspace header).</summary>
public static class SessionPresenter
{
    public static string StateLine(LaunchSession s, InventorySnapshot inv, bool modded) => s.State switch
    {
        _ when !s.IsBusy && s.ReviewText is { Length: > 0 } review => LocalizeReviewText(review),   // pre-launch review (e.g. dependencies)
        SessionState.Running when s.StartedAt is { } t => Loc.TFormat("session.runningFor", Elapsed(t)),
        SessionState.Running => Loc.T("state.running"),
        SessionState.Launching or SessionState.Preparing => s.StatusText,
        SessionState.SteamHandoff => Loc.T("session.viaSteam"),
        SessionState.Failed => s.StatusText.Length > 0 ? s.StatusText : Loc.T("state.failed"),
        // N-1: the sticky "Added <dependency> for <plugin>" notice — checked AFTER Failed/SteamHandoff
        // (neither is in IsBusy, so without this ordering the notice would hide a launch failure's reason,
        // or Steam's "launched via Steam") — but still BEFORE the plain Exited/idle fallbacks below, which
        // is exactly where a notice is meant to show: once the launch has settled with nothing more urgent
        // to report. Never cleared by Begin/the review's own end, so it's actually visible.
        _ when !s.IsBusy && s.DependencyNotice is { Length: > 0 } notice => LocalizeReviewText(notice),
        SessionState.Exited => Loc.T("state.exited"),
        _ => !inv.FolderExists ? Loc.T("session.folderMissing") : modded ? Loc.T("state.idle") : Loc.T("session.idleVanilla"),
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

    /// <summary>See <see cref="ReviewLines.Localize"/>.</summary>
    public static string LocalizeReviewText(string text) => ReviewLines.Localize(text);

    /// <summary>"Stable" / "Testing" in the active language (the stored channel value stays the identifier). Shared by
    /// the rail, dashboard tiles, the plugin matrix and the workspace header.</summary>
    public static string ChannelLabel(string? channel) => channel == "testing" ? Loc.T("channel.testing") : Loc.T("channel.stable");

    public static string Elapsed(DateTimeOffset since)
    {
        var d = DateTimeOffset.UtcNow - since;
        return d.TotalHours >= 1 ? Loc.TFormat("time.hoursMinutes", (int)d.TotalHours, d.Minutes) : Loc.TFormat("time.minutes", Math.Max(0, (int)d.TotalMinutes));
    }
}

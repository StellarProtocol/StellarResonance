using System;
using Avalonia.Media;
using System.Text.RegularExpressions;
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

    private static readonly Regex Added = new(@"^Added (.+) for (.+)$", RegexOptions.CultureInvariant);
    private static readonly Regex Preparing = new(@"^Preparing (.+?): (.+)…$", RegexOptions.CultureInvariant);

    /// <summary>The dependency review's progress lines ("Preparing &lt;plugin&gt;: &lt;deps&gt;…", "Added &lt;dep&gt; for
    /// &lt;plugin&gt;", N-2-joined with " · ") stay ENGLISH end to end: they are the protocol <see cref="ClientSessions"/>
    /// classifies the sticky notice on and the always-on log line, and the R-2/N-2 pins assert them verbatim. They are
    /// rendered in the active language only here, at display time; an unrecognised segment passes through unchanged.</summary>
    public static string LocalizeReviewText(string text)
    {
        var parts = text.Split(" · ");
        for (var i = 0; i < parts.Length; i++)
        {
            if (Added.Match(parts[i]) is { Success: true } a) parts[i] = Loc.TFormat("deps.added", a.Groups[1].Value, a.Groups[2].Value);
            else if (Preparing.Match(parts[i]) is { Success: true } p) parts[i] = Loc.TFormat("deps.preparing", p.Groups[1].Value, p.Groups[2].Value);
        }
        return string.Join(" · ", parts);
    }

    public static string Elapsed(DateTimeOffset since)
    {
        var d = DateTimeOffset.UtcNow - since;
        return d.TotalHours >= 1 ? Loc.TFormat("time.hoursMinutes", (int)d.TotalHours, d.Minutes) : Loc.TFormat("time.minutes", Math.Max(0, (int)d.TotalMinutes));
    }
}

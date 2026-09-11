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
        SessionState.Running when s.StartedAt is { } t => $"running · {Elapsed(t)}",
        SessionState.Running => "running",
        SessionState.Launching or SessionState.Preparing => s.StatusText,
        SessionState.SteamHandoff => "launched via Steam",
        SessionState.Failed => s.StatusText.Length > 0 ? s.StatusText : "failed",
        SessionState.Exited => "exited",
        _ => !inv.FolderExists ? "folder missing" : modded ? "idle" : "idle · launches vanilla until the framework is installed",
    };

    public static IBrush StateBrush(LaunchSession s, InventorySnapshot inv) => new SolidColorBrush(Color.Parse(s.State switch
    {
        SessionState.Running or SessionState.SteamHandoff => "#54e3a0",
        SessionState.Launching or SessionState.Preparing => "#9ec2ff",
        SessionState.Failed => "#ff9a9a",
        _ => inv.FolderExists ? "#697297" : "#ff9a9a",
    }));

    public static string Elapsed(DateTimeOffset since)
    {
        var d = DateTimeOffset.UtcNow - since;
        return d.TotalHours >= 1 ? $"{(int)d.TotalHours} h {d.Minutes} min" : $"{Math.Max(0, (int)d.TotalMinutes)} min";
    }
}

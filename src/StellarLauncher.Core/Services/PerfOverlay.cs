// src/StellarLauncher.Core/Services/PerfOverlay.cs
using System;
using System.IO;

namespace StellarLauncher.Core.Services;

/// <summary>Linux performance-overlay mode for the game launch.</summary>
public enum PerfOverlayMode
{
    /// <summary>No overlay.</summary>
    Off,
    /// <summary>DXVK's built-in FPS counter (<c>DXVK_HUD=fps</c>). Ships with Proton — no install.</summary>
    Fps,
    /// <summary>Full MangoHud overlay (GPU/CPU load, FPS, frametime graph). Needs the system
    /// <c>mangohud</c> package; activated via the Vulkan implicit layer (<c>MANGOHUD=1</c>).</summary>
    Full,
}

/// <summary>
/// MangoHud discovery for the Full overlay mode. MangoHud installs a Vulkan *implicit layer*
/// that any Vulkan process activates when <c>MANGOHUD=1</c> is set — no wrapper binary needed,
/// which is what makes it work under Proton/Wine/umu unchanged.
/// </summary>
public static class MangoHud
{
    /// <summary>
    /// Preset used when the user has no MangoHud config of their own: the compact
    /// GPU% / CPU% / FPS+frametime / frame-timing-graph layout. A user's
    /// <c>~/.config/MangoHud/MangoHud.conf</c> always wins (we don't set
    /// <c>MANGOHUD_CONFIG</c> when one exists — see <see cref="UserConfigExists"/>).
    /// </summary>
    public const string DefaultPreset = "fps,frametime,cpu_stats,gpu_stats,frame_timing";

    /// <summary>True when a MangoHud Vulkan implicit-layer manifest (or the CLI wrapper) is
    /// present on this system. Used only for the Settings hint — setting <c>MANGOHUD=1</c>
    /// without MangoHud installed is harmlessly inert.</summary>
    public static bool IsInstalled()
    {
        try
        {
            foreach (var dir in LayerDirs())
                if (Directory.Exists(dir) && Directory.GetFiles(dir, "MangoHud*.json").Length > 0)
                    return true;

            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var p in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                if (File.Exists(Path.Combine(p, "mangohud")))
                    return true;
        }
        catch { /* discovery is best-effort; the hint just stays visible */ }
        return false;
    }

    /// <summary>True when the user maintains their own MangoHud config — in that case the
    /// launcher must NOT set <c>MANGOHUD_CONFIG</c> (the env var would override their file).</summary>
    public static bool UserConfigExists()
    {
        try
        {
            var custom = Environment.GetEnvironmentVariable("MANGOHUD_CONFIGFILE");
            if (!string.IsNullOrEmpty(custom) && File.Exists(custom)) return true;

            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var confHome = string.IsNullOrEmpty(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                : xdg;
            return File.Exists(Path.Combine(confHome, "MangoHud", "MangoHud.conf"));
        }
        catch { return false; }
    }

    private static string[] LayerDirs()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var dataHome = string.IsNullOrEmpty(xdgData) ? Path.Combine(home, ".local", "share") : xdgData;
        return new[]
        {
            "/usr/share/vulkan/implicit_layer.d",
            "/usr/local/share/vulkan/implicit_layer.d",
            Path.Combine(dataHome, "vulkan", "implicit_layer.d"),
        };
    }
}

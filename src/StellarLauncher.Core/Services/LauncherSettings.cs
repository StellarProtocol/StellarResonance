// src/StellarLauncher.Core/Services/LauncherSettings.cs
using System.Collections.Generic;

namespace StellarLauncher.Core.Services;

public sealed class LauncherSettings
{
    public string? GameMiniDir { get; set; }
    public string? Runner { get; set; }      // Linux only
    public string? WinePrefix { get; set; }  // Linux only
    public bool Modded { get; set; } = true;
    public bool PluginsGridView { get; set; } = true;   // Plugins page: card grid (default) vs list rows
    public bool AutoUpdateBeforeLaunch { get; set; } = true;   // update framework + plugins before launch; ON by default
    public List<string> ExtraPluginRepos { get; set; } = new();
    public string Channel { get; set; } = "stable";   // "stable" | "testing"
    public bool DebugLogging { get; set; } = false;    // off = prod BepInEx.cfg (no console, buffered); on = console + crash-flush
    public int LastInteropCount { get; set; } = 0;     // interop assemblies generated last run; seeds the launch-progress estimate

    // Linux launch tweaks (applied when launching through the Wine/Proton runner).
    public bool Esync { get; set; } = true;            // WINEESYNC / PROTON_NO_ESYNC
    public bool Fsync { get; set; } = true;            // WINEFSYNC / PROTON_NO_FSYNC
    public bool FpsOverlay { get; set; } = false;      // LEGACY (pre-1.2.26 "Show FPS"); read only as the
                                                       // fallback in EffectiveOverlay(), written for downgrade compat
    public string PerfOverlay { get; set; } = "";      // "off" | "fps" | "full"; "" = unset -> legacy FpsOverlay
    public bool StellarPerf { get; set; } = false;     // STELLAR_PERFHUD=1 (framework's in-game perf overlay)
    public bool DxvkNvapi { get; set; } = true;        // auto install/update DXVK-NVAPI into the prefix

    /// <summary>Resolve the overlay mode: the new tri-state key wins; an unset key falls back to
    /// the legacy <see cref="FpsOverlay"/> checkbox so pre-1.2.26 settings keep their behavior.</summary>
    public PerfOverlayMode EffectiveOverlay() => PerfOverlay switch
    {
        "off"  => PerfOverlayMode.Off,
        "fps"  => PerfOverlayMode.Fps,
        "full" => PerfOverlayMode.Full,
        _      => FpsOverlay ? PerfOverlayMode.Fps : PerfOverlayMode.Off,
    };

    /// <summary>Store an overlay mode: writes the tri-state key and mirrors the legacy bool so a
    /// downgraded launcher still shows the closest equivalent.</summary>
    public void SetOverlay(PerfOverlayMode mode)
    {
        PerfOverlay = mode switch
        {
            PerfOverlayMode.Fps  => "fps",
            PerfOverlayMode.Full => "full",
            _                    => "off",
        };
        FpsOverlay = mode != PerfOverlayMode.Off;
    }
}

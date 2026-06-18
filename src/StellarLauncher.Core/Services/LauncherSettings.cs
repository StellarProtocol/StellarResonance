// src/StellarLauncher.Core/Services/LauncherSettings.cs
using System.Collections.Generic;

namespace StellarLauncher.Core.Services;

public sealed class LauncherSettings
{
    public string? GameMiniDir { get; set; }
    public string? Runner { get; set; }      // Linux only
    public string? WinePrefix { get; set; }  // Linux only
    public bool Modded { get; set; } = true;
    public List<string> ExtraPluginRepos { get; set; } = new();
    public string Channel { get; set; } = "stable";   // "stable" | "testing"
    public bool DebugLogging { get; set; } = false;    // off = prod BepInEx.cfg (no console, buffered); on = console + crash-flush
    public int LastInteropCount { get; set; } = 0;     // interop assemblies generated last run; seeds the launch-progress estimate

    // Linux launch tweaks (applied when launching through the Wine/Proton runner).
    public bool Esync { get; set; } = true;            // WINEESYNC / PROTON_NO_ESYNC
    public bool Fsync { get; set; } = true;            // WINEFSYNC / PROTON_NO_FSYNC
    public bool FpsOverlay { get; set; } = false;      // DXVK_HUD=fps (DXVK built-in FPS counter, no install)
    public bool StellarPerf { get; set; } = false;     // STELLAR_PERFHUD=1 (framework's in-game perf overlay)
    public bool DxvkNvapi { get; set; } = true;        // auto install/update DXVK-NVAPI into the prefix
}

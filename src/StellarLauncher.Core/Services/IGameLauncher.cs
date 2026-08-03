// src/StellarLauncher.Core/Services/IGameLauncher.cs
using System.Diagnostics;

namespace StellarLauncher.Core.Services;

/// <param name="Runner">Linux only: path to the proton/wine binary.</param>
/// <param name="WinePrefix">Linux only: WINEPREFIX.</param>
/// <param name="UmuRun">Linux only: path to umu-run, preferred for launching with Proton.</param>
/// <param name="Esync">Linux: enable esync (WINEESYNC / Proton default).</param>
/// <param name="Fsync">Linux: enable fsync (WINEFSYNC / Proton default).</param>
/// <param name="Overlay">Linux: performance overlay — <see cref="PerfOverlayMode.Fps"/> uses DXVK's
/// built-in counter (DXVK_HUD=fps, no install); <see cref="PerfOverlayMode.Full"/> activates MangoHud
/// via its Vulkan implicit layer (MANGOHUD=1; inert when MangoHud isn't installed).</param>
/// <param name="MangoHudPreset">Linux + Full overlay: value for <c>MANGOHUD_CONFIG</c>. Pass null when
/// the user has their own MangoHud config file so it is respected (the env var would override it).</param>
/// <param name="StellarPerf">Linux: enable the framework's Stellar Perf overlay sampling (STELLAR_PERFHUD).</param>
/// <param name="DxvkNvapi">Linux: add nvapi DLL overrides so the prefix's DXVK-NVAPI is used.</param>
/// <param name="SteamAppId">Windows + Steam install: launch via steam://rungameid/&lt;id&gt; so the game
/// gets a real Steam session (fixes "file not found" and restores combat traffic). The doorstop proxy
/// already in the game folder still injects the mod. Null = launch the exe directly.</param>
public sealed record LaunchRequest(
    string StarLauncherExe, string? Runner = null, string? WinePrefix = null, string? UmuRun = null,
    bool Esync = false, bool Fsync = false, PerfOverlayMode Overlay = PerfOverlayMode.Off,
    string? MangoHudPreset = null, bool DxvkNvapi = false,
    bool StellarPerf = false, string? SteamAppId = null);

public interface IGameLauncher
{
    ProcessStartInfo BuildStartInfo(LaunchRequest request);

    /// <summary>Start the game; returns the spawned process (null if it failed to start).</summary>
    Process? Launch(LaunchRequest request);
}

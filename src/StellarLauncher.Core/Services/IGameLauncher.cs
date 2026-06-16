// src/StellarLauncher.Core/Services/IGameLauncher.cs
using System.Diagnostics;

namespace StellarLauncher.Core.Services;

/// <param name="Runner">Linux only: path to the proton/wine binary.</param>
/// <param name="WinePrefix">Linux only: WINEPREFIX.</param>
/// <param name="UmuRun">Linux only: path to umu-run, preferred for launching with Proton.</param>
/// <param name="Esync">Linux: enable esync (WINEESYNC / Proton default).</param>
/// <param name="Fsync">Linux: enable fsync (WINEFSYNC / Proton default).</param>
/// <param name="FpsOverlay">Linux: show the MangoHud FPS overlay.</param>
/// <param name="DxvkNvapi">Linux: add nvapi DLL overrides so the prefix's DXVK-NVAPI is used.</param>
public sealed record LaunchRequest(
    string StarLauncherExe, string? Runner = null, string? WinePrefix = null, string? UmuRun = null,
    bool Esync = false, bool Fsync = false, bool FpsOverlay = false, bool DxvkNvapi = false);

public interface IGameLauncher
{
    ProcessStartInfo BuildStartInfo(LaunchRequest request);

    /// <summary>Start the game; returns the spawned process (null if it failed to start).</summary>
    Process? Launch(LaunchRequest request);
}

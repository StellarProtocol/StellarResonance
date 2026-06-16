// src/StellarLauncher.Core/Services/GameLauncher.cs
using System;
using System.Diagnostics;
using System.IO;
using StellarLauncher.Core.Platform;

namespace StellarLauncher.Core.Services;

public sealed class GameLauncher : IGameLauncher
{
    private readonly IPlatformInfo _platform;
    public GameLauncher(IPlatformInfo platform) => _platform = platform;

    public ProcessStartInfo BuildStartInfo(LaunchRequest r)
    {
        if (_platform.IsWindows)
            return new ProcessStartInfo
            {
                FileName = r.StarLauncherExe,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(r.StarLauncherExe) ?? "",
            };

        if (string.IsNullOrEmpty(r.Runner) || string.IsNullOrEmpty(r.WinePrefix))
            throw new InvalidOperationException("Linux launch needs a Runner + WINEPREFIX (set them in Settings)");

        var psi = new ProcessStartInfo { UseShellExecute = false };

        // load the BepInEx doorstop proxy; with DXVK-NVAPI on, also prefer the prefix's nvapi
        // (native, then builtin — safe whether or not the DLLs are actually installed).
        var overrides = "winhttp=n,b";
        if (r.DxvkNvapi) overrides += ";nvapi,nvapi64=n,b";
        psi.Environment["WINEDLLOVERRIDES"] = overrides;

        // esync/fsync (Wine vars; Proton enables both by default so we only flip the NO_* knobs off).
        psi.Environment["WINEESYNC"] = r.Esync ? "1" : "0";
        psi.Environment["WINEFSYNC"] = r.Fsync ? "1" : "0";
        if (r.FpsOverlay)
        {
            psi.Environment["DXVK_HUD"] = "fps";        // DXVK's built-in FPS counter (ships with Proton, no install)
            psi.Environment["STELLAR_PERFHUD"] = "1";   // also make the framework's Stellar Perf overlay sample (else it reads 0)
        }

        var isProton = Path.GetFileName(r.Runner).Contains("proton", StringComparison.OrdinalIgnoreCase);
        if (isProton)
        {
            if (!r.Esync) psi.Environment["PROTON_NO_ESYNC"] = "1";
            if (!r.Fsync) psi.Environment["PROTON_NO_FSYNC"] = "1";
        }

        if (isProton && !string.IsNullOrEmpty(r.UmuRun))
        {
            // Proton via umu-launcher — the correct way to run a non-Steam exe with Proton
            // against an arbitrary Wine prefix (umu sets up the Steam compat environment).
            psi.FileName = r.UmuRun;
            psi.ArgumentList.Add(r.StarLauncherExe);
            psi.Environment["WINEPREFIX"] = r.WinePrefix;
            psi.Environment["PROTONPATH"] = Path.GetDirectoryName(r.Runner) ?? r.Runner;
            psi.Environment["GAMEID"] = "0";     // non-Steam title
            psi.Environment["STORE"] = "none";
        }
        else if (isProton)
        {
            // Bare Proton fallback (no umu found): best effort via the Steam-compat verb.
            psi.FileName = r.Runner;
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add(r.StarLauncherExe);
            psi.Environment["STEAM_COMPAT_DATA_PATH"] = r.WinePrefix;
            psi.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = r.WinePrefix;
        }
        else
        {
            // Plain Wine.
            psi.FileName = r.Runner;
            psi.ArgumentList.Add(r.StarLauncherExe);
            psi.Environment["WINEPREFIX"] = r.WinePrefix;
        }

        return psi;
    }

    public Process? Launch(LaunchRequest request) => Process.Start(BuildStartInfo(request));
}

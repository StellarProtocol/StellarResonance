using System.Collections.Generic;
using StellarLauncher.Core.Services;

namespace StellarLauncher.Core.Clients;

/// <summary>Linux-only runtime for one client. Absent on Windows profiles.</summary>
public sealed class LinuxRuntime
{
    public string? Runner { get; set; }        // proton / wine binary
    public string? WinePrefix { get; set; }
    public bool Esync { get; set; } = true;
    public bool Fsync { get; set; } = true;
    public string PerfOverlay { get; set; } = "off";   // "off" | "fps" | "full"
    public bool DxvkNvapi { get; set; } = true;
    public bool StellarPerf { get; set; }

    public PerfOverlayMode OverlayMode() => PerfOverlay switch
    {
        "fps" => PerfOverlayMode.Fps,
        "full" => PerfOverlayMode.Full,
        _ => PerfOverlayMode.Off,
    };
}

/// <summary>Power-user launch options (Heroic-style), per client.</summary>
public sealed class AdvancedOptions
{
    public string? Wrapper { get; set; }
    public string? GameArgs { get; set; }
    public string? PreLaunch { get; set; }
    public bool PreLaunchWait { get; set; } = true;   // true = block launch until the script exits; false = run it alongside the game (killed on exit)
    public string? PostExit { get; set; }
    public List<EnvVar> Env { get; set; } = new();
}

/// <summary>One game install and everything the launcher knows about launching it.</summary>
public sealed class ClientProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Accent { get; set; } = AccentPalette.Defaults[0];
    public string GameMiniDir { get; set; } = "";
    public string Channel { get; set; } = "stable";      // "stable" | "testing"
    public bool Modded { get; set; } = true;
    public bool AutoUpdateBeforeLaunch { get; set; } = true;
    public bool DebugLogging { get; set; }
    public LinuxRuntime? Linux { get; set; }
    public AdvancedOptions Advanced { get; set; } = new();
    public int LastInteropCount { get; set; }
}

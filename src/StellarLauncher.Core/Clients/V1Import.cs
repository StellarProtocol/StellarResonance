using System.Linq;
using StellarLauncher.Core.Services;

namespace StellarLauncher.Core.Clients;

/// <summary>One-way conversion of the flat single-client settings (launcher ≤ 1.x) into v2.
/// Spec § 6: name from the install path, first default accent, v1 Channel feeds BOTH the client
/// and the launcher channel, ExtraPluginRepos become launcher plugin sources.</summary>
public static class V1Import
{
    public static LauncherConfig Convert(LauncherSettings v1, bool isWindows)
    {
        var cfg = new LauncherConfig();
        cfg.Launcher.Channel = ChannelManifests.IsTesting(v1.Channel) ? "testing" : "stable";
        cfg.Launcher.PluginSources = v1.ExtraPluginRepos.ToList();
        if (string.IsNullOrWhiteSpace(v1.GameMiniDir)) return cfg;

        cfg.Clients.Add(new ClientProfile
        {
            Id = ClientIds.New(),
            Name = ClientNaming.ProposeName(v1.GameMiniDir),
            Accent = AccentPalette.Defaults[0],
            GameMiniDir = v1.GameMiniDir,
            Channel = cfg.Launcher.Channel,
            Modded = v1.Modded,
            AutoUpdateBeforeLaunch = v1.AutoUpdateBeforeLaunch,
            DebugLogging = v1.DebugLogging,
            LastInteropCount = v1.LastInteropCount,
            Linux = isWindows ? null : LinuxFrom(v1),
            Advanced = new AdvancedOptions
            {
                Wrapper = v1.WrapperCommand, GameArgs = v1.GameArguments,
                PreLaunch = v1.PreLaunchScript, PostExit = v1.PostExitScript,
                Env = v1.ExtraEnv.ToList(),
            },
        });
        return cfg;
    }

    private static LinuxRuntime LinuxFrom(LauncherSettings v1) => new()
    {
        Runner = v1.Runner, WinePrefix = v1.WinePrefix,
        Esync = v1.Esync, Fsync = v1.Fsync,
        PerfOverlay = v1.EffectiveOverlay() switch
        {
            PerfOverlayMode.Fps => "fps",
            PerfOverlayMode.Full => "full",
            _ => "off",
        },
        DxvkNvapi = v1.DxvkNvapi, StellarPerf = v1.StellarPerf,
    };
}

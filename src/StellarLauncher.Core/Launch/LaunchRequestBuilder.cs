using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Services;

namespace StellarLauncher.Core.Launch;

public static class LaunchRequestBuilder
{
    public static LaunchRequest Build(ClientProfile c, string exe, string? umuRun, string? steamAppId, bool mangoHudUserConfig)
    {
        var lx = c.Linux;
        var overlay = lx?.OverlayMode() ?? PerfOverlayMode.Off;
        return new LaunchRequest(exe, lx?.Runner, lx?.WinePrefix, umuRun,
            Esync: lx?.Esync ?? false, Fsync: lx?.Fsync ?? false, Overlay: overlay,
            MangoHudPreset: overlay == PerfOverlayMode.Full && !mangoHudUserConfig ? MangoHud.DefaultPreset : null,
            DxvkNvapi: lx?.DxvkNvapi ?? false, StellarPerf: lx?.StellarPerf ?? false, SteamAppId: steamAppId,
            WrapperCommand: c.Advanced.Wrapper, GameArguments: c.Advanced.GameArgs, ExtraEnv: c.Advanced.Env);
    }
}

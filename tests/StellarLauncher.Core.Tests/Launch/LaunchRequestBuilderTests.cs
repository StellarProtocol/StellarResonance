using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Launch;
using StellarLauncher.Core.Services;
using Xunit;

public class LaunchRequestBuilderTests
{
    [Fact]
    public void Linux_profile_maps_every_field()
    {
        var c = new ClientProfile
        {
            GameMiniDir = "/g/game_mini",
            Linux = new LinuxRuntime { Runner = "/p/proton", WinePrefix = "/g", Esync = true, Fsync = false, PerfOverlay = "full", DxvkNvapi = true, StellarPerf = true },
            Advanced = new AdvancedOptions { Wrapper = "gamemoderun", GameArgs = "--x", Env = { new EnvVar { Name = "A", Value = "1" } } },
        };
        var r = LaunchRequestBuilder.Build(c, "/g/StarLauncher.exe", "/usr/bin/umu-run", null, mangoHudUserConfig: false);

        Assert.Equal("/g/StarLauncher.exe", r.StarLauncherExe);
        Assert.Equal("/p/proton", r.Runner); Assert.Equal("/g", r.WinePrefix); Assert.Equal("/usr/bin/umu-run", r.UmuRun);
        Assert.True(r.Esync); Assert.False(r.Fsync);
        Assert.Equal(PerfOverlayMode.Full, r.Overlay);
        Assert.Equal(MangoHud.DefaultPreset, r.MangoHudPreset);
        Assert.True(r.DxvkNvapi); Assert.True(r.StellarPerf); Assert.Null(r.SteamAppId);
        Assert.Equal("gamemoderun", r.WrapperCommand); Assert.Equal("--x", r.GameArguments);
        Assert.Equal("A", r.ExtraEnv![0].Name);
    }

    [Fact]
    public void User_mangohud_config_suppresses_the_preset()
    {
        var c = new ClientProfile { Linux = new LinuxRuntime { PerfOverlay = "full" } };
        Assert.Null(LaunchRequestBuilder.Build(c, "x.exe", null, null, mangoHudUserConfig: true).MangoHudPreset);
    }

    [Fact]
    public void Windows_profile_has_no_linux_fields_and_carries_steam_id()
    {
        var c = new ClientProfile { GameMiniDir = @"D:\S\steamapps\common\BP" };
        var r = LaunchRequestBuilder.Build(c, @"D:\S\steamapps\common\BP\StarSEA_STEAM.exe", null, "1234", false);
        Assert.Null(r.Runner); Assert.Null(r.WinePrefix); Assert.False(r.Esync);
        Assert.Equal(PerfOverlayMode.Off, r.Overlay); Assert.Equal("1234", r.SteamAppId);
    }
}

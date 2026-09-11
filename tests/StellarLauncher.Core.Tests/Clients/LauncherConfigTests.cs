using StellarLauncher.Core.Clients;
using Xunit;

public class LauncherConfigTests
{
    private static ClientProfile Client(string id, string name) => new() { Id = id, Name = name, GameMiniDir = "/g/" + id };

    [Fact]
    public void Defaults_match_spec()
    {
        var c = new ClientProfile();
        Assert.Equal("stable", c.Channel);
        Assert.True(c.Modded);
        Assert.True(c.AutoUpdateBeforeLaunch);
        Assert.False(c.DebugLogging);
        Assert.Null(c.Linux);
        Assert.Empty(c.Advanced.Env);
        Assert.Equal(0, c.LastInteropCount);

        var cfg = new LauncherConfig();
        Assert.Equal(2, cfg.Version);
        Assert.Equal("stable", cfg.Launcher.Channel);
        Assert.True(cfg.Launcher.KeepOpen);
        Assert.Equal("dashboard", cfg.Launcher.StartOn);
        Assert.True(cfg.Launcher.ShowMatrix);
        Assert.Empty(cfg.Clients);
    }

    [Fact]
    public void FindById_and_NameTaken()
    {
        var cfg = new LauncherConfig();
        cfg.Clients.Add(Client("c1", "Main"));
        cfg.Clients.Add(Client("c2", "Test"));

        Assert.Same(cfg.Clients[1], cfg.FindById("c2"));
        Assert.Null(cfg.FindById("nope"));
        Assert.True(cfg.NameTaken("main"));                 // case-insensitive
        Assert.False(cfg.NameTaken("Main", exceptId: "c1")); // renaming yourself is fine
        Assert.False(cfg.NameTaken("Third"));
    }

    [Fact]
    public void LinuxRuntime_overlay_mode_parses_tri_state()
    {
        Assert.Equal(StellarLauncher.Core.Services.PerfOverlayMode.Fps, new LinuxRuntime { PerfOverlay = "fps" }.OverlayMode());
        Assert.Equal(StellarLauncher.Core.Services.PerfOverlayMode.Full, new LinuxRuntime { PerfOverlay = "full" }.OverlayMode());
        Assert.Equal(StellarLauncher.Core.Services.PerfOverlayMode.Off, new LinuxRuntime { PerfOverlay = "garbage" }.OverlayMode());
    }
}

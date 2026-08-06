using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;
using Xunit;

public class SettingsStoreTests
{
    private sealed class FakePlatform : IPlatformInfo
    {
        public bool IsWindows => false;
        public string AppDataDir => "/cfg";
    }

    [Fact]
    public void Round_trips_settings()
    {
        var fs = new MockFileSystem();
        var store = new SettingsStore(fs, new FakePlatform());

        store.Save(new LauncherSettings
        {
            GameMiniDir = "/game/release_2.11/game_mini",
            Runner = "/opt/proton/proton",
            WinePrefix = "/home/u/.prefix",
            Modded = true,
        });

        var loaded = store.Load();
        Assert.Equal("/game/release_2.11/game_mini", loaded.GameMiniDir);
        Assert.Equal("/opt/proton/proton", loaded.Runner);
        Assert.True(loaded.Modded);
    }

    [Fact]
    public void Load_returns_defaults_when_absent()
    {
        var store = new SettingsStore(new MockFileSystem(), new FakePlatform());
        Assert.Null(store.Load().GameMiniDir);
    }

    [Fact]
    public void Load_returns_defaults_when_corrupt()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/cfg/stellar-launcher/settings.json", new MockFileData("{ this is not json"));
        var store = new SettingsStore(fs, new FakePlatform());
        Assert.Null(store.Load().GameMiniDir);
        Assert.True(store.Load().Modded);  // default
    }

    [Fact]
    public void AutoUpdateBeforeLaunch_defaults_on_and_round_trips()
    {
        var fs = new System.IO.Abstractions.TestingHelpers.MockFileSystem();
        var store = new StellarLauncher.Core.Services.SettingsStore(fs, new StellarLauncher.Core.Platform.PlatformInfo());

        // default is ON for a fresh install
        Assert.True(store.Load().AutoUpdateBeforeLaunch);

        var cfg = store.Load();
        cfg.AutoUpdateBeforeLaunch = false;
        store.Save(cfg);
        Assert.False(store.Load().AutoUpdateBeforeLaunch);
    }
}

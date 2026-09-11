using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;
using Xunit;

public class ConfigStoreTests
{
    private sealed class Linux : IPlatformInfo { public bool IsWindows => false; public string AppDataDir => "/cfg"; }
    private sealed class Windows : IPlatformInfo { public bool IsWindows => true; public string AppDataDir => @"C:\cfg"; }

    private const string V1Json = """
    {
      "GameMiniDir": "/opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_3.7/game_mini",
      "Runner": "/home/u/.config/heroic/tools/proton/GE-Proton10-26/proton",
      "WinePrefix": "/opt/game/BlueProtocol2",
      "Modded": false,
      "PluginsGridView": false,
      "AutoUpdateBeforeLaunch": false,
      "ExtraPluginRepos": ["https://example.org/plugins.json"],
      "Channel": "testing",
      "DebugLogging": true,
      "LastInteropCount": 193,
      "Esync": true, "Fsync": false,
      "FpsOverlay": true, "PerfOverlay": "",
      "StellarPerf": true, "DxvkNvapi": false,
      "WrapperCommand": "gamemoderun", "GameArguments": "--foo",
      "PreLaunchScript": "/s/pre.sh", "PostExitScript": null,
      "ExtraEnv": [ { "Name": "STELLAR_WIRECAP", "Value": "all" } ]
    }
    """;

    [Fact]
    public void Missing_file_loads_empty_config_and_writes_nothing()
    {
        var fs = new MockFileSystem();
        var store = new ConfigStore(fs, new Linux());
        var cfg = store.Load();
        Assert.Empty(cfg.Clients);
        Assert.Equal(2, cfg.Version);
        Assert.False(fs.File.Exists("/cfg/stellar-launcher/settings.json"));
    }

    [Fact]
    public void V2_round_trips_with_camelCase_and_no_nulls()
    {
        var fs = new MockFileSystem();
        var store = new ConfigStore(fs, new Linux());
        var cfg = new LauncherConfig();
        cfg.Clients.Add(new ClientProfile { Id = "c1", Name = "Main", GameMiniDir = "/g/game_mini",
            Linux = new LinuxRuntime { Runner = "/p/proton", WinePrefix = "/g" } });
        cfg.Launcher.LastSelectedClientId = "c1";
        store.Save(cfg);

        var text = fs.File.ReadAllText("/cfg/stellar-launcher/settings.json");
        Assert.Contains("\"version\": 2", text);
        Assert.Contains("\"gameMiniDir\"", text);
        Assert.DoesNotContain("\"wrapper\": null", text);   // WhenWritingNull

        var back = store.Load();
        Assert.Equal("Main", back.Clients[0].Name);
        Assert.Equal("/p/proton", back.Clients[0].Linux!.Runner);
        Assert.Equal("c1", back.Launcher.LastSelectedClientId);
    }

    [Fact]
    public void V1_file_is_imported_once_backed_up_and_rewritten_as_v2()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/cfg/stellar-launcher/settings.json", new MockFileData(V1Json));
        var store = new ConfigStore(fs, new Linux());

        var cfg = store.Load();

        var c = Assert.Single(cfg.Clients);
        Assert.Equal("BlueProtocol2", c.Name);
        Assert.Equal(8, c.Id.Length);
        Assert.Equal("#37c8e0", c.Accent);
        Assert.Equal("/opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_3.7/game_mini", c.GameMiniDir);
        Assert.Equal("testing", c.Channel);
        Assert.Equal("testing", cfg.Launcher.Channel);           // v1 had ONE switch for both
        Assert.False(c.Modded);
        Assert.False(c.AutoUpdateBeforeLaunch);
        Assert.True(c.DebugLogging);
        Assert.Equal(193, c.LastInteropCount);
        Assert.Equal("/home/u/.config/heroic/tools/proton/GE-Proton10-26/proton", c.Linux!.Runner);
        Assert.Equal("/opt/game/BlueProtocol2", c.Linux.WinePrefix);
        Assert.True(c.Linux.Esync); Assert.False(c.Linux.Fsync);
        Assert.Equal("fps", c.Linux.PerfOverlay);                 // legacy FpsOverlay=true, PerfOverlay unset
        Assert.True(c.Linux.StellarPerf); Assert.False(c.Linux.DxvkNvapi);
        Assert.Equal("gamemoderun", c.Advanced.Wrapper);
        Assert.Equal("--foo", c.Advanced.GameArgs);
        Assert.Equal("/s/pre.sh", c.Advanced.PreLaunch);
        Assert.Null(c.Advanced.PostExit);
        Assert.Equal("STELLAR_WIRECAP", Assert.Single(c.Advanced.Env).Name);
        Assert.Equal(new[] { "https://example.org/plugins.json" }, cfg.Launcher.PluginSources);

        Assert.Equal(V1Json, fs.File.ReadAllText("/cfg/stellar-launcher/settings.v1.json"));
        Assert.Contains("\"version\": 2", fs.File.ReadAllText("/cfg/stellar-launcher/settings.json"));

        // second load is the v2 path — same client id, backup untouched
        var again = store.Load();
        Assert.Equal(c.Id, again.Clients[0].Id);
    }

    [Fact]
    public void V1_on_windows_has_no_linux_block()
    {
        var fs = new MockFileSystem();
        fs.AddFile(@"C:\cfg\stellar-launcher\settings.json",
            new MockFileData("""{ "GameMiniDir": "E:\\bpsr\\StarLauncher\\game\\release_3.7\\game_mini", "Channel": "stable" }"""));
        var cfg = new ConfigStore(fs, new Windows()).Load();
        var c = Assert.Single(cfg.Clients);
        Assert.Equal("bpsr", c.Name);
        Assert.Null(c.Linux);
    }

    [Fact]
    public void V1_without_game_dir_imports_zero_clients_but_keeps_channel()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/cfg/stellar-launcher/settings.json", new MockFileData("""{ "Channel": "testing" }"""));
        var cfg = new ConfigStore(fs, new Linux()).Load();
        Assert.Empty(cfg.Clients);
        Assert.Equal("testing", cfg.Launcher.Channel);
    }

    [Fact]
    public void Corrupt_file_loads_empty_and_is_left_in_place()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/cfg/stellar-launcher/settings.json", new MockFileData("{ not json"));
        var cfg = new ConfigStore(fs, new Linux()).Load();
        Assert.Empty(cfg.Clients);
        Assert.Equal("{ not json", fs.File.ReadAllText("/cfg/stellar-launcher/settings.json"));
    }

    [Fact]
    public void ClientIds_are_8_hex_chars_and_unique()
    {
        var a = ClientIds.New(); var b = ClientIds.New();
        Assert.Matches("^[0-9a-f]{8}$", a);
        Assert.NotEqual(a, b);
    }
}

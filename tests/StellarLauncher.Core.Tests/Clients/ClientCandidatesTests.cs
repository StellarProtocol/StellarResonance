using System.Collections.Generic;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;
using Xunit;

public class ClientCandidatesTests
{
    private const string Main = "/opt/game/BlueProtocol/drive_c/Star/StarLauncher/game/release_3.7/game_mini";
    private const string Test = "/opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_3.7/game_mini";
    private const string Asia = "/home/u/Games/Heroic/Prefixes/StarASIA/drive_c/StarLauncher/game/release_3.7/game_mini";

    private sealed class FakeDetector(IReadOnlyList<string> found, string? runner) : IGameDetector
    {
        public int RunnerCalls;
        public IReadOnlyList<string> Detect() => found;
        public string? DetectRunner() { RunnerCalls++; return runner; }
        public string? DetectUmu() => null;
    }
    private sealed class Linux : IPlatformInfo { public bool IsWindows => false; public string AppDataDir => "/cfg"; }
    private sealed class Windows : IPlatformInfo { public bool IsWindows => true; public string AppDataDir => @"C:\cfg"; }

    [Fact]
    public void Excludes_configured_and_dismissed_paths_and_derives_linux_runtime()
    {
        var det = new FakeDetector(new[] { Main, Test, Asia }, "/p/GE-Proton10-26/proton");
        var cfg = new LauncherConfig();
        cfg.Clients.Add(new ClientProfile { Id = "c1", Name = "Main", GameMiniDir = Main + "/" });   // trailing slash still matches
        cfg.Launcher.DismissedDetections.Add(Test);

        var found = new ClientCandidates(det, new Linux()).Find(cfg);

        var c = Assert.Single(found);
        Assert.Equal(Asia, c.GameMiniDir);
        Assert.Equal("StarASIA", c.ProposedName);
        Assert.Equal(ClientLayout.JpStarLauncher, c.Layout);
        Assert.True(c.IsWinePrefix);
        Assert.Equal("/home/u/Games/Heroic/Prefixes/StarASIA", c.WinePrefix);
        Assert.Equal("/p/GE-Proton10-26/proton", c.Runner);
        Assert.Equal(1, det.RunnerCalls);   // detected once, not per candidate
    }

    [Fact]
    public void Proposed_names_are_unique_against_config_and_each_other()
    {
        var det = new FakeDetector(new[] { "/a/BlueProtocol/drive_c/Star/StarLauncher/game/release_3.7/game_mini",
                                          "/b/BlueProtocol/drive_c/Star/StarLauncher/game/release_3.7/game_mini" }, null);
        var cfg = new LauncherConfig();
        cfg.Clients.Add(new ClientProfile { Id = "x", Name = "BlueProtocol", GameMiniDir = "/elsewhere/game_mini" });

        var found = new ClientCandidates(det, new Linux()).Find(cfg);

        Assert.Equal(new[] { "BlueProtocol 2", "BlueProtocol 3" }, found.Select(f => f.ProposedName).ToArray());
    }

    [Fact]
    public void Windows_paths_compare_case_insensitively_and_have_no_wine_runtime()
    {
        var det = new FakeDetector(new[] { @"E:\bpsr\StarLauncher\game\release_3.7\game_mini" }, null);
        var cfg = new LauncherConfig();
        cfg.Clients.Add(new ClientProfile { Id = "c1", Name = "bpsr", GameMiniDir = @"e:\BPSR\StarLauncher\game\release_3.7\game_mini" });
        Assert.Empty(new ClientCandidates(det, new Windows()).Find(cfg));
        Assert.NotNull(new ClientCandidates(det, new Windows()).ConfiguredFor(cfg, @"E:\bpsr\StarLauncher\game\release_3.7\game_mini"));
    }

    [Fact]
    public void ToProfile_assigns_next_accent_id_and_linux_block()
    {
        var det = new FakeDetector(new[] { Asia }, "/p/proton");
        var cfg = new LauncherConfig();
        cfg.Clients.Add(new ClientProfile { Id = "c1", Name = "Main", Accent = "#37c8e0", GameMiniDir = Main });
        var cands = new ClientCandidates(det, new Linux());

        var p = cands.ToProfile(cands.Find(cfg)[0], cfg);

        Assert.Equal("StarASIA", p.Name);
        Assert.Equal("#ffb347", p.Accent);
        Assert.Matches("^[0-9a-f]{8}$", p.Id);
        Assert.Equal(Asia, p.GameMiniDir);
        Assert.Equal("/p/proton", p.Linux!.Runner);
        Assert.Equal("/home/u/Games/Heroic/Prefixes/StarASIA", p.Linux.WinePrefix);
        Assert.Equal("stable", p.Channel);
    }
}

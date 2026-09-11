using System.Diagnostics;
using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels.Dashboard;
using StellarLauncher.App.ViewModels.Shell;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Launch;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;
using Xunit;

public class DashboardViewModelTests
{
    private const string Main = "/opt/game/BlueProtocol/drive_c/Star/StarLauncher/game/release_3.7/game_mini";
    private const string Asia = "/home/u/Games/Heroic/Prefixes/StarASIA/drive_c/StarLauncher/game/release_3.7/game_mini";
    private const string Found = "/opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_3.7/game_mini";

    private sealed class Platform : IPlatformInfo { public bool IsWindows => false; public string AppDataDir => "/cfg"; }
    private sealed class Registry : IPluginRegistryService
    {
        public static readonly PluginEntry Cm = new("combatmeter", "CombatMeter", "d", "a",
            new[] { new PluginVersion("2.10.0", null, "Stellar.CombatMeter.dll", "https://cdn/Stellar.CombatMeter.dll", "sha", "2.0.0", null, null) });
        public Task<IReadOnlyList<PluginEntry>> FetchAllAsync(IEnumerable<Uri> u, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PluginEntry>>(new[] { Cm });
    }
    private sealed class Versions : IVersionService
    {
        public Task<FrameworkManifest> FetchAsync(Uri url, CancellationToken ct = default) => Task.FromResult(new FrameworkManifest("2.8.0", "stable",
            new[] { new VersionManifest("2.8.0", "2026-09-09", "https://cdn/b.zip", "sha", "1.0.0", new Changelog(new[] { "x" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>())) }));
    }
    private sealed class Detector : IGameDetector
    {
        public IReadOnlyList<string> Detect() => new[] { Main, Asia, Found };
        public string? DetectRunner() => "/p/proton"; public string? DetectUmu() => null;
    }
    private sealed class Review : IPreLaunchReview { public Task<bool> ReviewAsync(ClientProfile c, CancellationToken ct) => Task.FromResult(true); }
    private sealed class Orch : ILaunchOrchestrator
    {
        public readonly List<string> Launched = new();
        public Task<LaunchOutcome> LaunchAsync(ClientProfile c, IProgress<LaunchEvent> e, CancellationToken ct) { Launched.Add(c.Id); e.Report(new RunningEvent()); return Task.FromResult(new LaunchOutcome(LaunchOutcomeKind.Started, null)); }
    }
    private sealed class NoScan : IRunningProcessScanner { public IReadOnlyList<RunningProcess> Snapshot() => Array.Empty<RunningProcess>(); }
    private sealed class NoProc : IProcessFactory { public IGameProcess? Start(ProcessStartInfo p) => null; public IGameProcess? Attach(int pid) => null; }

    private static (DashboardViewModel dash, ShellViewModel shell, Orch orch) Build()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{Main}/BepInEx/plugins/Stellar.Framework/.stellar-version", new MockFileData("2.7.4"));
        fs.AddFile($"{Main}/stellar/plugins/combatmeter/Stellar.CombatMeter.dll", new MockFileData("x"));
        fs.AddFile($"{Main}/stellar/plugins/combatmeter/.plugin-version", new MockFileData("2.10.0"));
        fs.AddDirectory(Asia);
        var store = new ConfigStore(fs, new Platform());
        var cfg = new LauncherConfig();
        cfg.Clients.Add(new ClientProfile { Id = "c1", Name = "Main", GameMiniDir = Main, Accent = "#37c8e0", Linux = new LinuxRuntime { Runner = "/p/GE-Proton10-34/proton" } });
        cfg.Clients.Add(new ClientProfile { Id = "c2", Name = "StarASIA", GameMiniDir = Asia, Accent = "#ff6ec7", Modded = false });
        store.Save(cfg);
        var orch = new Orch();
        var sessions = new ClientSessions(store, orch, new NoScan(), new NoProc(), () => DateTimeOffset.UnixEpoch, a => a());
        var deps = new PluginInstallDeps(new Installer(fs), new PluginInstaller(fs), new HttpClient());
        var svc = new DashboardServices(
            new StellarLauncher.Core.Inventory.ClientInventory(fs, deps.Installer, deps.Plugins, new DoorstopToggle(fs)),
            new RegistryCache(new Registry(), () => store.Load()), new FrameworkManifests(new Versions()),
            new Review(), deps, new ClientCandidates(new Detector(), new Platform()));
        ShellViewModel shell = null!;
        shell = new ShellViewModel(store, sessions, new ShellPages(s => new DashboardViewModel(s, svc), (s, c) => new object(), s => new object(), s => new object()));
        shell.Start();
        return ((DashboardViewModel)shell.Current!, shell, orch);
    }

    [Fact]
    public async Task Refresh_builds_tiles_matrix_summary_and_candidate_badge()
    {
        var (dash, shell, _) = Build();
        await dash.RefreshAsync();

        Assert.Equal(2, dash.Tiles.Count);
        var main = dash.Tiles[0];
        Assert.Equal("Main", main.Name);
        Assert.Equal("fw 2.7.4 · 2.8.0 available", main.FrameworkLine);
        Assert.Equal("1 plugin · up to date", main.PluginsLine);
        Assert.Equal("GE-Proton10-34", main.RunnerTag);
        Assert.Equal("Modded", main.ModeTag);
        Assert.True(main.IsLaunchVisible);
        var asia = dash.Tiles[1];
        Assert.Equal("fw not installed", asia.FrameworkLine);
        Assert.True(asia.ShowInstallFramework);
        Assert.Equal("Vanilla", asia.ModeTag);

        Assert.Equal(2, dash.Columns.Count);
        Assert.Equal("fw 2.7.4 · stable", dash.Columns[0].Subtitle);
        Assert.Equal("no framework", dash.Columns[1].Subtitle);
        var row = Assert.Single(dash.Rows);
        Assert.Equal("CombatMeter", row.Name);
        Assert.Equal("2.10.0 ✓", row.Cells[0].Label);
        Assert.Equal("needs framework", row.Cells[1].Label);
        Assert.False(row.Cells[1].IsClickable);

        Assert.StartsWith("2 clients · 0 running", dash.SummaryLine);
        Assert.Contains("framework 2.8.0 is the latest stable", dash.SummaryLine);
        Assert.Equal(1, shell.CandidateCount);                     // BlueProtocol2 detected, not configured
        Assert.Equal("2.7.4", shell.Clients[0].SummaryFramework);
    }

    [Fact]
    public async Task Launch_from_tile_goes_through_sessions_and_updates_state()
    {
        var (dash, shell, orch) = Build();
        await dash.RefreshAsync();
        await dash.Tiles[0].LaunchCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "c1" }, orch.Launched);
        Assert.Equal(SessionState.Running, shell.Sessions.For(shell.Config.Clients[0]).State);
        Assert.True(dash.Tiles[0].IsRunningVisible);
        Assert.Contains("1 running", dash.SummaryLine);
    }

    [Fact]
    public async Task SessionChanged_refreshes_tiles()
    {
        var (dash, shell, _) = Build();
        await dash.RefreshAsync();
        var eventCount = 0;
        dash.Tiles[0].PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ClientTileViewModel.StateLine)) eventCount++; };

        var session = shell.Sessions.For(shell.Config.Clients[0]);
        session.Begin(DateTimeOffset.UnixEpoch);
        session.Apply(new PreparingEvent());

        Assert.True(eventCount > 0, "StateLine should have changed when session transitioned");
    }

    [Fact]
    public async Task Offline_framework_manifest_shows_in_summary()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{Main}/BepInEx/plugins/Stellar.Framework/.stellar-version", new MockFileData("2.7.4"));
        var store = new ConfigStore(fs, new Platform());
        var cfg = new LauncherConfig();
        cfg.Clients.Add(new ClientProfile { Id = "c1", Name = "Main", GameMiniDir = Main, Accent = "#37c8e0" });
        store.Save(cfg);

        var sessions = new ClientSessions(store, new NoOrch(), new NoScan(), new NoProc(), () => DateTimeOffset.UnixEpoch, a => a());
        var deps = new PluginInstallDeps(new Installer(fs), new PluginInstaller(fs), new HttpClient());

        // IVersionService that throws (offline scenario)
        var failingVersions = new ThrowingVersions();

        var svc = new DashboardServices(
            new StellarLauncher.Core.Inventory.ClientInventory(fs, deps.Installer, deps.Plugins, new DoorstopToggle(fs)),
            new RegistryCache(new Registry(), () => store.Load()), new FrameworkManifests(failingVersions),
            new Review(), deps, new ClientCandidates(new Detector(), new Platform()));

        ShellViewModel shell = null!;
        shell = new ShellViewModel(store, sessions, new ShellPages(s => new DashboardViewModel(s, svc), (s, c) => new object(), s => new object(), s => new object()));
        shell.Start();
        var dash = (DashboardViewModel)shell.Current!;

        await dash.RefreshAsync();

        Assert.Contains("framework manifest offline", dash.SummaryLine);
    }

    private sealed class NoOrch : ILaunchOrchestrator
    {
        public Task<LaunchOutcome> LaunchAsync(ClientProfile c, IProgress<LaunchEvent> e, CancellationToken ct) => Task.FromResult(new LaunchOutcome(LaunchOutcomeKind.Failed, null));
    }

    private sealed class ThrowingVersions : IVersionService
    {
        public Task<FrameworkManifest> FetchAsync(Uri url, CancellationToken ct = default) => throw new HttpRequestException("offline");
    }
}

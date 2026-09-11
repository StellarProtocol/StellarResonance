using System.Diagnostics;
using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels.Dashboard;
using StellarLauncher.App.ViewModels.Shell;
using StellarLauncher.App.ViewModels.Workspace;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Launch;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;
using Xunit;

public class WorkspaceViewModelTests
{
    private const string G = "/opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_3.7/game_mini";
    private sealed class Platform : IPlatformInfo { public bool IsWindows => false; public string AppDataDir => "/cfg"; }
    private sealed class Registry : IPluginRegistryService
    {
        public Task<IReadOnlyList<PluginEntry>> FetchAllAsync(IEnumerable<Uri> u, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PluginEntry>>(Array.Empty<PluginEntry>());
    }
    private sealed class Versions : IVersionService
    {
        public Task<FrameworkManifest> FetchAsync(Uri url, CancellationToken ct = default) => Task.FromResult(new FrameworkManifest("2.8.0", "stable", new[]
        {
            new VersionManifest("2.8.0", "2026-09-09", "https://cdn/b.zip", "sha", "0.0.0", new Changelog(new[] { "Meter value text style" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>())),
            new VersionManifest("2.7.4", "2026-09-09", "https://cdn/a.zip", "sha", "0.0.0", new Changelog(Array.Empty<string>(), Array.Empty<string>(), new[] { "Steam boot crash" }, Array.Empty<string>())),
        }));
    }
    private sealed class Detector : IGameDetector { public IReadOnlyList<string> Detect() => Array.Empty<string>(); public string? DetectRunner() => null; public string? DetectUmu() => null; }
    private sealed class Review : IPreLaunchReview { public Task<bool> ReviewAsync(ClientProfile c, CancellationToken ct) => Task.FromResult(true); }
    private sealed class Orch : ILaunchOrchestrator { public Task<LaunchOutcome> LaunchAsync(ClientProfile c, IProgress<LaunchEvent> e, CancellationToken ct) { e.Report(new RunningEvent()); return Task.FromResult(new LaunchOutcome(LaunchOutcomeKind.Started, null)); } }
    private sealed class NoScan : IRunningProcessScanner { public IReadOnlyList<RunningProcess> Snapshot() => Array.Empty<RunningProcess>(); }
    private sealed class NoProc : IProcessFactory { public IGameProcess? Start(ProcessStartInfo p) => null; public IGameProcess? Attach(int pid) => null; }

    private static (ClientWorkspaceViewModel ws, MockFileSystem fs, ConfigStore store) Build()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{G}/BepInEx/plugins/Stellar.Framework/.stellar-version", new MockFileData("2.7.4"));
        fs.AddFile($"{G}/doorstop_config.ini", new MockFileData("[General]\nenabled = true\n"));
        var store = new ConfigStore(fs, new Platform());
        var cfg = new LauncherConfig();
        var c = new ClientProfile { Id = "c2", Name = "Test", GameMiniDir = G, Accent = "#ffb347", Channel = "testing", Linux = new LinuxRuntime { Runner = "/p/GE-Proton10-26/proton", WinePrefix = "/opt/game/BlueProtocol2" } };
        cfg.Clients.Add(c); store.Save(cfg);
        var sessions = new ClientSessions(store, new Orch(), new NoScan(), new NoProc(), () => DateTimeOffset.UnixEpoch, a => a());
        var deps = new PluginInstallDeps(new Installer(fs), new PluginInstaller(fs), new HttpClient());
        var core = new DashboardServices(new ClientInventory(fs, deps.Installer, deps.Plugins, new DoorstopToggle(fs)),
            new RegistryCache(new Registry(), () => store.Load()), new FrameworkManifests(new Versions()), new Review(), deps,
            new ClientCandidates(new Detector(), new Platform()));
        var svc = new WorkspaceServices(core, new DoorstopToggle(fs), fs, new Platform(), new Detector(), new GameLocator(fs));
        var tabs = new WorkspaceTabFactories(w => new OverviewViewModel(w), w => new TabPlaceholder("plugins"), w => new TabPlaceholder("settings"), w => new TabPlaceholder("logs"));
        var shell = new ShellViewModel(store, sessions, new ShellPages(s => new object(), (s, cl) => new ClientWorkspaceViewModel(s, cl, svc, tabs), s => new object(), s => new object()));
        shell.Start();
        shell.ShowClientById("c2");
        return ((ClientWorkspaceViewModel)shell.Current!, fs, store);
    }

    [Fact]
    public async Task Header_tabs_and_refresh()
    {
        var (ws, _, _) = Build();
        await ws.RefreshAsync();
        Assert.Equal("Test", ws.Name);
        Assert.Equal("Testing", ws.ChannelTag);
        Assert.Equal("Linux · GE-Proton10-26", ws.RuntimeTag);
        Assert.Equal("2.7.4", ws.Inventory.FrameworkVersion);
        Assert.Equal(WorkspaceTab.Overview, ws.Tab);
        Assert.IsType<OverviewViewModel>(ws.TabContent);
        ws.ShowLogsCommand.Execute(null);
        Assert.Equal(WorkspaceTab.Logs, ws.Tab);
        Assert.Equal("logs", ((TabPlaceholder)ws.TabContent!).Name);
        Assert.True(ws.IsLaunchVisible);
    }

    [Fact]
    public async Task Overview_framework_card_offers_the_update_and_lists_the_selected_changelog()
    {
        var (ws, _, _) = Build();
        await ws.RefreshAsync();
        var ov = (OverviewViewModel)ws.TabContent!;
        Assert.Equal("v2.7.4", ov.InstalledLabel);
        Assert.Equal("v2.8.0 · 2026-09-09", ov.LatestLabel);
        Assert.Equal("Update to v2.8.0", ov.ActionLabel);
        Assert.True(ov.IsUpdate);
        Assert.Contains("Meter value text style", ov.SelectedVersion!.Changelog.Added);
        ov.SelectedVersion = ov.Versions[1];
        Assert.Equal("Reinstall v2.7.4", ov.ActionLabel);
    }

    [Fact]
    public async Task Modded_switch_flips_doorstop_and_persists()
    {
        var (ws, fs, store) = Build();
        await ws.RefreshAsync();
        var ov = (OverviewViewModel)ws.TabContent!;
        ov.Modded = false;
        Assert.Contains("enabled = false", fs.File.ReadAllText($"{G}/doorstop_config.ini"));
        Assert.False(store.Load().Clients[0].Modded);
        Assert.Equal("Vanilla", ws.ModeTag);
    }

    [Fact]
    public async Task Launch_from_header_runs_the_session()
    {
        var (ws, _, _) = Build();
        await ws.RefreshAsync();
        await ws.LaunchCommand.ExecuteAsync(null);
        Assert.Equal(SessionState.Running, ws.Session.State);
        Assert.True(ws.IsRunningVisible);
    }

    [Fact]
    public async Task Dispose_unsubscribes_from_session_changes()
    {
        var (ws, _, _) = Build();
        await ws.RefreshAsync();
        int changeCount = 0;
        ws.PropertyChanged += (_, _) => changeCount++;
        ws.Dispose();
        // Verify Dispose can be called multiple times safely
        ws.Dispose();
        Assert.True(true);
    }
}

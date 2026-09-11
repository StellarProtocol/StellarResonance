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
    // minLauncherVersion "0.0.0": dev builds report AppInfo.LauncherVersion = "0.0.0", so the manifest must not demand more.
    private sealed class Versions(string minLauncher = "0.0.0") : IVersionService
    {
        public Task<FrameworkManifest> FetchAsync(Uri url, CancellationToken ct = default) => Task.FromResult(new FrameworkManifest("2.8.0", "stable", new[]
        {
            new VersionManifest("2.8.0", "2026-09-09", "https://cdn/b.zip", "sha", minLauncher, new Changelog(new[] { "Meter value text style" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>())),
            new VersionManifest("2.7.4", "2026-09-09", "https://cdn/a.zip", "sha", minLauncher, new Changelog(Array.Empty<string>(), Array.Empty<string>(), new[] { "Steam boot crash" }, Array.Empty<string>())),
        }));
    }
    private sealed class DisposableTab : IDisposable { public bool Disposed; public void Dispose() => Disposed = true; }
    private sealed class Detector : IGameDetector { public IReadOnlyList<string> Detect() => Array.Empty<string>(); public string? DetectRunner() => null; public string? DetectUmu() => null; }
    private sealed class Review : IPreLaunchReview { public Task<bool> ReviewAsync(ClientProfile c, CancellationToken ct) => Task.FromResult(true); }
    private sealed class Orch : ILaunchOrchestrator { public Task<LaunchOutcome> LaunchAsync(ClientProfile c, IProgress<LaunchEvent> e, CancellationToken ct) { e.Report(new RunningEvent()); return Task.FromResult(new LaunchOutcome(LaunchOutcomeKind.Started, null)); } }
    private sealed class NoScan : IRunningProcessScanner { public IReadOnlyList<RunningProcess> Snapshot() => Array.Empty<RunningProcess>(); }
    private sealed class NoProc : IProcessFactory { public IGameProcess? Start(ProcessStartInfo p) => null; public IGameProcess? Attach(int pid) => null; }

    private static (ClientWorkspaceViewModel ws, MockFileSystem fs, ConfigStore store) Build(IVersionService? versions = null, Func<ClientWorkspaceViewModel, object>? pluginsTab = null)
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
            new RegistryCache(new Registry(), () => store.Load()), new FrameworkManifests(versions ?? new Versions()), new Review(), deps,
            new ClientCandidates(new Detector(), new Platform()));
        var svc = new WorkspaceServices(core, new DoorstopToggle(fs), fs, new Platform(), new Detector(), new GameLocator(fs));
        var tabs = new WorkspaceTabFactories(w => new OverviewViewModel(w), pluginsTab ?? (w => new TabPlaceholder("plugins")), w => new TabPlaceholder("settings"), w => new TabPlaceholder("logs"));
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
        var before = 0; ws.PropertyChanged += (_, _) => before++;
        await ws.LaunchCommand.ExecuteAsync(null);          // session change → header raises
        Assert.True(before > 0, "the subscription was live before Dispose");

        ws.Dispose(); ws.Dispose();                          // idempotent
        var after = 0; ws.PropertyChanged += (_, _) => after++;
        ws.Session.Apply(new FailedEvent("probe"));          // a real state change (Stop() is a no-op without a process)
        Assert.Equal(SessionState.Failed, ws.Session.State);
        Assert.Equal(0, after);
    }

    [Fact]
    public async Task Dispose_cascades_to_tab_view_models_built_so_far()
    {
        var tab = new DisposableTab();
        var (ws, _, _) = Build(pluginsTab: _ => tab);
        await ws.RefreshAsync();
        ws.ShowPluginsCommand.Execute(null);
        Assert.Same(tab, ws.TabContent);
        ws.Dispose();
        Assert.True(tab.Disposed);
    }

    [Fact]
    public async Task Active_tab_carries_the_accent_underline_and_the_others_are_transparent()
    {
        var (ws, _, _) = Build();
        await ws.RefreshAsync();
        Assert.NotSame(Avalonia.Media.Brushes.Transparent, ws.OverviewUnderline);
        Assert.Same(Avalonia.Media.Brushes.Transparent, ws.LogsUnderline);
        ws.ShowLogsCommand.Execute(null);
        Assert.Same(Avalonia.Media.Brushes.Transparent, ws.OverviewUnderline);
        Assert.NotSame(Avalonia.Media.Brushes.Transparent, ws.LogsUnderline);
    }

    [Fact]
    public async Task AutoUpdate_and_DebugLogging_switches_write_the_live_profile_and_persist()
    {
        var (ws, _, store) = Build();
        await ws.RefreshAsync();
        var ov = (OverviewViewModel)ws.TabContent!;
        ov.AutoUpdate = true; ov.DebugLogging = true;
        Assert.True(ws.Client.AutoUpdateBeforeLaunch); Assert.True(ws.Client.DebugLogging);
        var stored = store.Load().Clients[0];
        Assert.True(stored.AutoUpdateBeforeLaunch); Assert.True(stored.DebugLogging);
    }

    [Fact]
    public async Task Too_old_launcher_keeps_one_disabled_button_visible_with_the_reason()
    {
        var (ws, _, _) = Build(versions: new Versions(minLauncher: "99.0.0"));
        await ws.RefreshAsync();
        var ov = (OverviewViewModel)ws.TabContent!;
        Assert.False(ov.CanChangeFramework);
        Assert.Equal("Update launcher first", ov.ActionLabel);
        Assert.True(ov.IsReinstall);                         // the ghost button renders (disabled) and carries the label
        Assert.False(ov.IsInstall); Assert.False(ov.IsUpdate);
    }
}

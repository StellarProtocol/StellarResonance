using System.Diagnostics;
using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels.Shell;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Launch;
using StellarLauncher.Core.Platform;
using Xunit;

public class ShellViewModelTests
{
    private sealed class Platform : IPlatformInfo { public bool IsWindows => false; public string AppDataDir => "/cfg"; }
    private sealed class NoOrch : ILaunchOrchestrator
    {
        public Task<LaunchOutcome> LaunchAsync(ClientProfile c, IProgress<LaunchEvent> e, CancellationToken ct) => Task.FromResult(new LaunchOutcome(LaunchOutcomeKind.Failed, null));
    }
    private sealed class NoScan : IRunningProcessScanner { public IReadOnlyList<RunningProcess> Snapshot() => Array.Empty<RunningProcess>(); }
    private sealed class NoProc : IProcessFactory { public IGameProcess? Start(ProcessStartInfo p) => null; public IGameProcess? Attach(int pid) => null; }
    private sealed record Marker(string Kind, string? ClientId = null);

    private static readonly ShellPages Pages = new(
        Dashboard: _ => new Marker("dashboard"),
        Workspace: (_, c) => new Marker("workspace", c.Id),
        AddClient: _ => new Marker("add"),
        LauncherSettings: _ => new Marker("launcher"));

    private static (ShellViewModel shell, ConfigStore store, ClientSessions sessions) Build(params ClientProfile[] clients)
    {
        var store = new ConfigStore(new MockFileSystem(), new Platform());
        var cfg = new LauncherConfig(); cfg.Clients.AddRange(clients); store.Save(cfg);
        var sessions = new ClientSessions(store, new NoOrch(), new NoScan(), new NoProc(), () => DateTimeOffset.UnixEpoch, a => a());
        return (new ShellViewModel(store, sessions, Pages), store, sessions);
    }

    private static ClientProfile C(string id, string name) => new() { Id = id, Name = name, GameMiniDir = "/g/" + id, Accent = "#37c8e0" };

    [Fact]
    public void First_run_opens_add_client()
    {
        var (shell, _, _) = Build();
        shell.Start();
        Assert.Equal(ShellPage.AddClient, shell.Page);
        Assert.Equal("add", ((Marker)shell.Current!).Kind);
    }

    [Fact]
    public void With_clients_opens_dashboard_and_lists_them()
    {
        var (shell, _, _) = Build(C("c1", "Main"), C("c2", "Test"));
        shell.Start();
        Assert.Equal(ShellPage.Dashboard, shell.Page);
        Assert.Equal(new[] { "Main", "Test" }, shell.Clients.Select(i => i.Name).ToArray());
        Assert.Null(shell.SelectedClient);
    }

    [Fact]
    public void ShowClient_selects_persists_and_opens_the_workspace()
    {
        var (shell, store, _) = Build(C("c1", "Main"), C("c2", "Test"));
        shell.Start();
        shell.ShowClientCommand.Execute(shell.Clients[1]);
        Assert.Equal(ShellPage.Workspace, shell.Page);
        Assert.Equal("c2", ((Marker)shell.Current!).ClientId);
        Assert.True(shell.Clients[1].IsSelected); Assert.False(shell.Clients[0].IsSelected);
        Assert.Equal("c2", store.Load().Launcher.LastSelectedClientId);
    }

    [Fact]
    public void StartOn_lastClient_reopens_it()
    {
        var (shell, store, _) = Build(C("c1", "Main"), C("c2", "Test"));
        var cfg = store.Load(); cfg.Launcher.StartOn = "lastClient"; cfg.Launcher.LastSelectedClientId = "c2"; store.Save(cfg);
        shell.Start();
        Assert.Equal(ShellPage.Workspace, shell.Page);
        Assert.Equal("c2", shell.SelectedClient!.Client.Id);
    }

    [Fact]
    public void ClientAdded_and_ClientRemoved_update_rail_and_navigate()
    {
        var (shell, store, sessions) = Build(C("c1", "Main"));
        shell.Start();
        shell.ClientAdded(C("c9", "Asia"));
        Assert.Equal(2, shell.Clients.Count);
        Assert.Equal("c9", shell.SelectedClient!.Client.Id);
        Assert.Equal(2, store.Load().Clients.Count);

        _ = sessions.For(shell.Clients[1].Client);
        shell.ClientRemoved("c9");
        Assert.Single(shell.Clients);
        Assert.Null(sessions.TryGet("c9"));
        Assert.Equal(ShellPage.Dashboard, shell.Page);
    }

    [Fact]
    public void ClientEdited_keeps_profile_instances_live_so_repeated_edits_persist()
    {
        var (shell, store, _) = Build(C("c1", "Main"));
        shell.Start();
        shell.ShowClientCommand.Execute(shell.Clients[0]);
        var profile = shell.SelectedClient!.Client;           // the reference a workspace page holds

        profile.Name = "First"; shell.ClientEdited();
        profile.Name = "Second"; shell.ClientEdited();

        Assert.Same(profile, shell.Config.Clients[0]);
        Assert.Same(profile, shell.Clients[0].Client);
        Assert.Equal("Second", store.Load().FindById("c1")!.Name);
        Assert.True(shell.Clients[0].IsSelected);
    }

    [Fact]
    public void Session_changes_refresh_the_rail_row()
    {
        var (shell, _, sessions) = Build(C("c1", "Main"));
        shell.Start();
        var row = shell.Clients[0];
        Assert.Equal("idle", row.StateLabel);
        var s = sessions.For(row.Client);
        s.Begin(DateTimeOffset.UnixEpoch); s.Apply(new PreparingEvent());
        Assert.Equal("preparing", row.StateLabel);
        Assert.Contains("preparing", row.Summary);
        Assert.False(row.ShowQuickLaunch);
    }
}

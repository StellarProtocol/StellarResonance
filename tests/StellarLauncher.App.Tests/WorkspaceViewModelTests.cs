using StellarLauncher.App.ViewModels.Workspace;
using StellarLauncher.Core.Launch;
using Xunit;

public class WorkspaceViewModelTests
{
    private const string G = "/opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_3.7/game_mini";
    private sealed class DisposableTab : IDisposable { public bool Disposed; public void Dispose() => Disposed = true; }

    private static async Task<(ClientWorkspaceViewModel ws, WorkspaceFixture f)> Open(string minLauncher = "0.0.0", Func<ClientWorkspaceViewModel, object>? pluginsTab = null)
    {
        var f = new WorkspaceFixture { Manifests = new WorkspaceFixture.ManifestVersions(minLauncher) };
        if (pluginsTab is not null) f.Tabs = f.Tabs with { Plugins = pluginsTab };
        f.AddClient("c2", "Test", G, channel: "testing", accent: "#ffb347");
        f.Start();
        return (await f.OpenAsync("c2"), f);
    }

    [Fact]
    public async Task Header_tabs_and_refresh()
    {
        var (ws, _) = await Open();
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
        var (ws, _) = await Open();
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
        var (ws, f) = await Open();
        var ov = (OverviewViewModel)ws.TabContent!;
        ov.Modded = false;
        Assert.Contains("enabled = false", f.Fs.File.ReadAllText($"{G}/doorstop_config.ini"));
        Assert.False(f.Store.Load().Clients[0].Modded);
        Assert.Equal("Vanilla", ws.ModeTag);
    }

    [Fact]
    public async Task Launch_from_header_runs_the_session()
    {
        var (ws, _) = await Open();
        await ws.LaunchCommand.ExecuteAsync(null);
        Assert.Equal(SessionState.Running, ws.Session.State);
        Assert.True(ws.IsRunningVisible);
    }

    [Fact]
    public async Task Dispose_unsubscribes_from_session_changes()
    {
        var (ws, _) = await Open();
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
        var (ws, _) = await Open(pluginsTab: _ => tab);
        ws.ShowPluginsCommand.Execute(null);
        Assert.Same(tab, ws.TabContent);
        ws.Dispose();
        Assert.True(tab.Disposed);
    }

    [Fact]
    public async Task Active_tab_carries_the_accent_underline_and_the_others_are_transparent()
    {
        var (ws, _) = await Open();
        Assert.NotSame(Avalonia.Media.Brushes.Transparent, ws.OverviewUnderline);
        Assert.Same(Avalonia.Media.Brushes.Transparent, ws.LogsUnderline);
        ws.ShowLogsCommand.Execute(null);
        Assert.Same(Avalonia.Media.Brushes.Transparent, ws.OverviewUnderline);
        Assert.NotSame(Avalonia.Media.Brushes.Transparent, ws.LogsUnderline);
    }

    [Fact]
    public async Task AutoUpdate_and_DebugLogging_switches_write_the_live_profile_and_persist()
    {
        var (ws, f) = await Open();
        var ov = (OverviewViewModel)ws.TabContent!;
        ov.AutoUpdate = true; ov.DebugLogging = true;
        Assert.True(ws.Client.AutoUpdateBeforeLaunch); Assert.True(ws.Client.DebugLogging);
        var stored = f.Store.Load().Clients[0];
        Assert.True(stored.AutoUpdateBeforeLaunch); Assert.True(stored.DebugLogging);
    }

    [Fact]
    public async Task Too_old_launcher_keeps_one_disabled_button_visible_with_the_reason()
    {
        var (ws, _) = await Open(minLauncher: "99.0.0");
        var ov = (OverviewViewModel)ws.TabContent!;
        Assert.False(ov.CanChangeFramework);
        Assert.Equal("Update launcher first", ov.ActionLabel);
        Assert.True(ov.IsReinstall);                         // the ghost button renders (disabled) and carries the label
        Assert.False(ov.IsInstall); Assert.False(ov.IsUpdate);
    }
}

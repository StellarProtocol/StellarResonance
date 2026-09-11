using StellarLauncher.App.ViewModels.Shell;
using StellarLauncher.App.ViewModels.Workspace;
using Xunit;

public class ClientSettingsViewModelTests
{
    private const string Main = "/opt/game/BlueProtocol/drive_c/Star/StarLauncher/game/release_3.7/game_mini";
    private const string Test = "/opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_3.7/game_mini";

    private static async Task<(WorkspaceFixture f, ClientSettingsViewModel vm, ClientWorkspaceViewModel ws)> Open()
    {
        var f = new WorkspaceFixture();
        f.AddClient("c1", "Main", Main);
        f.AddClient("c2", "Test", Test, channel: "testing", accent: "#ffb347");
        f.Tabs = f.Tabs with { Settings = w => new ClientSettingsViewModel(w) };
        f.Start();
        var ws = await f.OpenAsync("c2");
        ws.ShowSettingsCommand.Execute(null);
        return (f, (ClientSettingsViewModel)ws.TabContent!, ws);
    }

    [Fact]
    public async Task Loads_profile_and_layout_line()
    {
        var (_, vm, _) = await Open();
        Assert.Equal("Test", vm.Name);
        Assert.True(vm.TestingChannel);
        Assert.True(vm.IsLinux);
        Assert.Equal("/p/GE-Proton10-26/proton", vm.Runner);
        Assert.Equal("/opt/game/BlueProtocol2", vm.WinePrefix);
        Assert.Contains("SEA · StarLauncher", vm.LayoutLine);
        Assert.Contains("release_3.7", vm.LayoutLine);
        Assert.True(vm.Swatches.Single(s => s.IsSelected).Hex == "#ffb347");
    }

    [Fact]
    public async Task Rename_persists_and_rejects_duplicates()
    {
        var (f, vm, _) = await Open();
        vm.Name = "Main";
        Assert.Contains("already", vm.Status);
        Assert.Equal("Test", f.Store.Load().Clients[1].Name);
        vm.Name = "Tester";
        Assert.Equal("Tester", f.Store.Load().Clients[1].Name);
        Assert.Equal("Tester", f.Shell.Clients[1].Name);
    }

    [Fact]
    public async Task Accent_channel_and_tweaks_persist()
    {
        var (f, vm, _) = await Open();
        vm.PickAccent("#ff6ec7");
        vm.TestingChannel = false;
        vm.Fsync = false;
        vm.PerfOverlayIndex = 1;
        vm.Wrapper = "gamemoderun";
        vm.AddEnvVarCommand.Execute(null);
        vm.EnvVars[0].Name = "STELLAR_WIRECAP"; vm.EnvVars[0].Value = "all";
        var c = f.Store.Load().Clients[1];
        Assert.Equal("#ff6ec7", c.Accent);
        Assert.Equal("stable", c.Channel);
        Assert.False(c.Linux!.Fsync);
        Assert.Equal("fps", c.Linux.PerfOverlay);
        Assert.Equal("gamemoderun", c.Advanced.Wrapper);
        Assert.Equal("all", Assert.Single(c.Advanced.Env).Value);
    }

    [Fact]
    public async Task Remove_client_forgets_the_entry_after_confirm_and_returns_to_dashboard()
    {
        var (f, vm, _) = await Open();
        await vm.RemoveClientCommand.ExecuteAsync(null);
        Assert.Equal(1, f.Confirm.Asked);
        Assert.Single(f.Store.Load().Clients);
        Assert.Equal(ShellPage.Dashboard, f.Shell.Page);
        Assert.True(f.Fs.Directory.Exists(Test));   // nothing on disk deleted
    }
}

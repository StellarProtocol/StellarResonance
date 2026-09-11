using StellarLauncher.App.ViewModels.Workspace;
using Xunit;

public class ClientPluginsViewModelTests
{
    private const string Main = "/opt/game/BlueProtocol/drive_c/Star/StarLauncher/game/release_3.7/game_mini";
    private const string Test = "/opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_3.7/game_mini";

    private static async Task<(WorkspaceFixture f, ClientPluginsViewModel vm)> Open()
    {
        var f = new WorkspaceFixture();
        f.Registry.Add(WorkspaceFixture.Entry("combatmeter", "CombatMeter", "2.0.0", "2.10.0"));
        f.Registry.Add(WorkspaceFixture.Entry("minimalnameplate", "Minimal Nameplate", "2.0.0", "2.1.2", "2.1.1"));
        f.Registry.Add(WorkspaceFixture.Entry("playerhud", "PlayerHUD", "2.0.0", "2.1.0"));
        f.Registry.Add(WorkspaceFixture.Entry("wardrobe", "Wardrobe", "2.6.4", "1.3.0"));
        f.AddClient("c1", "Main", Main, framework: "2.8.0");
        f.AddClient("c2", "Test", Test, channel: "testing", framework: "2.7.4", accent: "#ffb347");
        f.InstallPlugin(Main, "combatmeter", "Stellar.CombatMeter.dll", "2.10.0");
        f.InstallPlugin(Main, "minimalnameplate", "Stellar.MinimalNameplate.dll", "2.1.2");
        f.InstallPlugin(Main, "playerhud", "Stellar.PlayerHUD.dll", "2.1.0");
        f.InstallPlugin(Main, "wardrobe", "Stellar.Wardrobe.dll", "1.3.0", disabled: true);
        f.InstallPlugin(Test, "combatmeter", "Stellar.CombatMeter.dll", "2.10.0");
        f.InstallPlugin(Test, "minimalnameplate", "Stellar.MinimalNameplate.dll", "2.1.1");
        f.Tabs = f.Tabs with { Plugins = w => new ClientPluginsViewModel(w) };
        f.Start();
        var ws = await f.OpenAsync("c2");
        ws.ShowPluginsCommand.Execute(null);
        var vm = (ClientPluginsViewModel)ws.TabContent!;
        await vm.ReloadAsync();
        return (f, vm);
    }

    [Fact]
    public async Task Rows_show_versions_updates_and_also_on_chips_for_this_client_only()
    {
        var (_, vm) = await Open();
        Assert.Equal(4, vm.Rows.Count);
        Assert.Contains("/opt/game/BlueProtocol2/", vm.TargetLine);
        var mn = vm.Rows.Single(r => r.Item.Entry.Id == "minimalnameplate");
        Assert.Equal("2.1.1 ↑ 2.1.2", mn.VersionLine); Assert.True(mn.HasUpdate);
        Assert.Equal("Main 2.1.2", Assert.Single(mn.AlsoOn).Text);
        var ph = vm.Rows.Single(r => r.Item.Entry.Id == "playerhud");
        Assert.Equal("2.1.0 · not installed", ph.VersionLine); Assert.False(ph.IsInstalled);
        var wd = vm.Rows.Single(r => r.Item.Entry.Id == "wardrobe");
        Assert.Equal("Main 1.3.0 · off", Assert.Single(wd.AlsoOn).Text);
        Assert.Equal(2, vm.InstalledCount); Assert.Equal(1, vm.UpdateCount); Assert.Equal(0, vm.DisabledCount);
        Assert.Equal("MN", mn.Initials);
    }

    [Fact]
    public async Task View_mode_toggles_and_persists_launcher_wide()
    {
        var (f, vm) = await Open();
        Assert.False(vm.GridView); Assert.True(vm.IsListView);      // default = list
        vm.ShowGridCommand.Execute(null);
        Assert.True(vm.GridView); Assert.False(vm.IsListView);
        Assert.True(f.Store.Load().Launcher.PluginsGridView);        // persisted
        vm.ShowListCommand.Execute(null);
        Assert.False(f.Store.Load().Launcher.PluginsGridView);
    }

    [Fact]
    public async Task Filters_and_search()
    {
        var (_, vm) = await Open();
        vm.SetFilterCommand.Execute(PluginFilter.Updates);
        Assert.Single(vm.Rows);
        vm.SetFilterCommand.Execute(PluginFilter.All);
        vm.Search = "player";
        Assert.Equal("PlayerHUD", Assert.Single(vm.Rows).Name);
    }

    [Fact]
    public async Task Install_update_disable_write_only_this_clients_folder()
    {
        var (f, vm) = await Open();
        var ph = vm.Rows.Single(r => r.Item.Entry.Id == "playerhud");
        await ph.InstallCommand.ExecuteAsync(null);
        Assert.True(f.Fs.File.Exists($"{Test}/stellar/plugins/playerhud/Stellar.PlayerHUD.dll"));
        Assert.Equal("2.1.0", f.Fs.File.ReadAllText($"{Test}/stellar/plugins/playerhud/.plugin-version"));

        var mn = vm.Rows.Single(r => r.Item.Entry.Id == "minimalnameplate");
        await mn.UpdateCommand.ExecuteAsync(null);
        Assert.Equal("2.1.2", f.Fs.File.ReadAllText($"{Test}/stellar/plugins/minimalnameplate/.plugin-version"));
        Assert.Equal("2.1.2", f.Fs.File.ReadAllText($"{Main}/stellar/plugins/minimalnameplate/.plugin-version"));   // untouched

        var cm = vm.Rows.Single(r => r.Item.Entry.Id == "combatmeter");
        cm.Enabled = false;
        Assert.True(f.Fs.Directory.Exists($"{Test}/stellar/plugins-disabled/combatmeter"));
        Assert.True(f.Fs.Directory.Exists($"{Main}/stellar/plugins/combatmeter"));
    }

    [Fact]
    public async Task Copy_set_from_Main_installs_what_Test_lacks_after_confirm()
    {
        var (f, vm) = await Open();
        var main = f.Shell.Config.Clients.Single(c => c.Id == "c1");
        await vm.CopySetFromCommand.ExecuteAsync(main);
        Assert.Equal(1, f.Confirm.Asked);
        Assert.True(f.Fs.File.Exists($"{Test}/stellar/plugins/playerhud/Stellar.PlayerHUD.dll"));
        Assert.False(f.Fs.Directory.Exists($"{Test}/stellar/plugins/wardrobe"));   // disabled on source → skipped
        Assert.Contains("1 installed", vm.Status);
    }
}

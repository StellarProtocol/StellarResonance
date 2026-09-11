using StellarLauncher.App.ViewModels.AddClient;
using StellarLauncher.App.ViewModels.Shell;
using Xunit;

public class AddClientViewModelTests
{
    private const string Main = "/opt/game/BlueProtocol/drive_c/Star/StarLauncher/game/release_3.7/game_mini";
    private const string Test = "/opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_3.7/game_mini";
    private const string Asia = "/home/u/Games/Heroic/Prefixes/StarASIA/drive_c/StarLauncher/game/release_3.7/game_mini";

    private static (WorkspaceFixture f, AddClientViewModel vm) Open(bool withMain)
    {
        var f = new WorkspaceFixture();
        f.GameDetector.Found.AddRange(new[] { Main, Test, Asia });
        foreach (var p in new[] { Main, Test, Asia }) f.Fs.AddDirectory(p);
        if (withMain) f.AddClient("c1", "Main", Main);
        f.Start();
        var vm = new AddClientViewModel(f.Shell, f.Services);
        return (f, vm);
    }

    [Fact]
    public void Lists_new_installs_selected_and_configured_ones_greyed()
    {
        var (_, vm) = Open(withMain: true);
        Assert.False(vm.IsFirstRun);
        Assert.Equal(3, vm.Rows.Count);
        var main = vm.Rows.Single(r => r.Path == Main);
        Assert.Equal("Main", main.AlreadyAddedAs); Assert.False(main.IsAvailable); Assert.False(main.Selected);
        var asia = vm.Rows.Single(r => r.Path == Asia);
        Assert.True(asia.Selected); Assert.Equal("StarASIA", asia.Name); Assert.Equal("JP layout", asia.LayoutTag);
        Assert.Contains("GE-Proton10-26", asia.RunnerLine);
        Assert.Equal("Add 2 clients", vm.AddLabel);
    }

    [Fact]
    public void Add_creates_profiles_with_unique_names_and_accents_then_goes_to_dashboard()
    {
        var (f, vm) = Open(withMain: true);
        vm.Rows.Single(r => r.Path == Test).Name = "Main";     // clash → suffixed on add
        vm.AddCommand.Execute(null);
        var clients = f.Store.Load().Clients;
        Assert.Equal(3, clients.Count);
        Assert.Equal(new[] { "Main", "Main 2", "StarASIA" }, clients.Select(c => c.Name).ToArray());
        Assert.Equal(3, clients.Select(c => c.Accent).Distinct().Count());
        Assert.Equal("/opt/game/BlueProtocol2", clients[1].Linux!.WinePrefix);
        Assert.Equal(ShellPage.Dashboard, f.Shell.Page);
    }

    [Fact]
    public void First_run_with_one_selected_opens_that_client()
    {
        var (f, vm) = Open(withMain: false);
        Assert.True(vm.IsFirstRun);
        foreach (var r in vm.Rows.Where(r => r.Path != Main)) r.Selected = false;
        vm.AddCommand.Execute(null);
        Assert.Equal(ShellPage.Workspace, f.Shell.Page);
        Assert.Equal("BlueProtocol", f.Shell.SelectedClient!.Client.Name);
    }

    [Fact]
    public void Hide_persists_dismissal_and_ShowHidden_restores()
    {
        var (f, vm) = Open(withMain: true);
        vm.HideCommand.Execute(vm.Rows.Single(r => r.Path == Test));
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal(1, vm.HiddenCount);
        Assert.Contains(Test, f.Store.Load().Launcher.DismissedDetections);
        vm.ShowHiddenCommand.Execute(null);
        Assert.Equal(3, vm.Rows.Count);
        Assert.Empty(f.Store.Load().Launcher.DismissedDetections);
    }

    [Fact]
    public void AddFromPath_refuses_configured_folders_and_adds_new_ones()
    {
        var (f, vm) = Open(withMain: true);
        vm.AddFromPath(Main);
        Assert.Contains("already added as Main", vm.Status);
        f.Fs.AddDirectory("/x/StarLauncher/game/release_3.7/game_mini");
        vm.AddFromPath("/x/StarLauncher/game");                                   // resolves to the newest release_*/game_mini
        Assert.Contains(vm.Rows, r => r.Path == "/x/StarLauncher/game/release_3.7/game_mini" && r.Selected);
    }

    [Fact]
    public void Browsed_row_gets_an_accent_no_other_row_or_client_uses()
    {
        var (f, vm) = Open(withMain: true);
        f.Fs.AddDirectory("/x/StarLauncher/game/release_3.7/game_mini");
        vm.AddFromPath("/x/StarLauncher/game");
        // Configured clients + every row that would CREATE a client (a greyed "already added" row shares its client's accent by design).
        var used = f.Shell.Config.Clients.Select(c => c.Accent).Concat(vm.Rows.Where(r => r.IsAvailable).Select(r => r.Accent)).ToList();
        Assert.Equal(4, used.Count);
        Assert.Equal(used.Count, used.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}

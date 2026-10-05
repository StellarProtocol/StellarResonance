using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels;
using StellarLauncher.App.ViewModels.Workspace;
using StellarLauncher.Core.Model;
using Xunit;

/// <summary>Launcher 2.1.2 (owner feedback — discoverability): (A) the DEPENDENCIES tick boxes explain
/// themselves while locked by the running game; (B) an installed plugin's optional extras get a compact
/// pill on its plugin-list row, which navigates to the detail page's DEPENDENCIES section.</summary>
public class Deps212DiscoverabilityTests
{
    private const string G = "/opt/game/X/drive_c/Star/StarLauncher/game/release_3.7/game_mini";

    private static PluginDependency Dep(string id, string name, bool optional) =>
        new(id, name, "1.0.0", $"https://cdn/deps/{id}", WorkspaceFixture.DllSha, WorkspaceFixture.DllBytes.Length, "file",
            new[] { new PluginDependencyFile(null, $"{id}.bin") }, "game", Optional: optional,
            License: "MIT", LicenseUrl: "https://l", SourceUrl: "https://s");

    private static PluginEntry Photo(params PluginDependency[] deps) => new("photo", "Photo Thing", "d", "StellarProtocol",
        new[] { new PluginVersion("1.0.0", null, "Stellar.Photo.dll", "https://cdn/photo/1.0.0.dll", WorkspaceFixture.DllSha, "2.0.0", null, null, Dependencies: deps) });

    private static async Task<(WorkspaceFixture f, ClientPluginsViewModel vm)> Open(PluginEntry entry)
    {
        var f = new WorkspaceFixture();
        f.Registry.Add(entry);
        f.AddClient("c1", "Main", G, framework: "2.8.0");
        f.InstallPlugin(G, entry.Id, "Stellar.Photo.dll", entry.Versions[0].Version);
        f.Tabs = f.Tabs with { Plugins = w => new ClientPluginsViewModel(w) };
        f.Start();
        var ws = await f.OpenAsync("c1");
        ws.ShowPluginsCommand.Execute(null);
        var vm = (ClientPluginsViewModel)ws.TabContent!;
        await vm.ReloadAsync();
        return (f, vm);
    }

    private static PluginRowViewModel Row(ClientPluginsViewModel vm) => vm.Rows.Single(r => r.Item.Entry.Id == "photo");
    private static PluginItemViewModel Item(ClientPluginsViewModel vm) => Row(vm).Item;

    // ---- (A) locked hint ----

    // Owner feedback: the tick boxes going grey with no explanation read as broken. The hint must appear
    // exactly when that confusion can happen — locked, with an optional dependency that WOULD be
    // changeable once the game closes — and nowhere else.
    [Fact]
    public async Task Locked_hint_shows_only_while_locked_with_an_optional_dependency()
    {
        var fx = Dep("fx", "Effects runtime", optional: true);
        var (f, vm) = await Open(Photo(fx));
        var item = Item(vm);
        vm.OpenPluginCommand.Execute(item);
        await item.DependencyRefresh;

        Assert.False(item.ShowLockedHint);   // not locked yet

        f.Shell.Sessions.For(f.Shell.Config.Clients.Single()).Begin(DateTimeOffset.UnixEpoch);   // Launching → IsBusy
        vm.OpenPluginCommand.Execute(item);   // re-open applies the new lock, same as the real page-open path
        await item.DependencyRefresh;

        Assert.True(item.ShowLockedHint);
    }

    [Fact]
    public async Task Locked_hint_never_shows_without_an_optional_dependency()
    {
        var req = Dep("req", "Required thing", optional: false);
        var (f, vm) = await Open(Photo(req));
        var item = Item(vm);
        f.Shell.Sessions.For(f.Shell.Config.Clients.Single()).Begin(DateTimeOffset.UnixEpoch);

        vm.OpenPluginCommand.Execute(item);
        await item.DependencyRefresh;

        Assert.False(item.ShowLockedHint);
    }

    [Fact]
    public async Task Locked_hint_hides_again_once_the_game_closes()
    {
        var fx = Dep("fx", "Effects runtime", optional: true);
        var (f, vm) = await Open(Photo(fx));
        var item = Item(vm);
        var session = f.Shell.Sessions.For(f.Shell.Config.Clients.Single());
        session.Begin(DateTimeOffset.UnixEpoch);
        vm.OpenPluginCommand.Execute(item);
        await item.DependencyRefresh;
        Assert.True(item.ShowLockedHint);

        item.DependenciesLocked = false;   // the session-changed handler does this once the game exits

        Assert.False(item.ShowLockedHint);
    }

    // ---- (B) extras pill ----

    [Fact]
    public async Task Pill_text_and_visibility_reflect_skip_state()
    {
        var a = Dep("a", "A", optional: true);
        var b = Dep("b", "B", optional: true);
        var (f, vm) = await Open(Photo(a, b));
        var item = Item(vm);

        Assert.True(item.ShowExtrasPill);
        Assert.Equal("Extras 2/2", item.ExtrasPillText);

        vm.SetDependencyUse(item, "b", use: false);
        await vm.DependencyWork;

        Assert.Equal("Extras 1/2", item.ExtrasPillText);
    }

    [Fact]
    public async Task Pill_is_hidden_for_a_plugin_without_optional_dependencies()
    {
        var req = Dep("req", "Required thing", optional: false);
        var (f, vm) = await Open(Photo(req));

        Assert.False(Item(vm).ShowExtrasPill);
    }

    [Fact]
    public async Task Pill_is_hidden_for_a_plugin_that_is_not_installed()
    {
        var a = Dep("a", "A", optional: true);
        var entry = Photo(a);
        var f = new WorkspaceFixture();
        f.Registry.Add(entry);
        f.AddClient("c1", "Main", G, framework: "2.8.0");
        // deliberately NOT installed
        f.Tabs = f.Tabs with { Plugins = w => new ClientPluginsViewModel(w) };
        f.Start();
        var ws = await f.OpenAsync("c1");
        ws.ShowPluginsCommand.Execute(null);
        var vm = (ClientPluginsViewModel)ws.TabContent!;
        await vm.ReloadAsync();

        Assert.False(Item(vm).ShowExtrasPill);
    }

    // The pill must actually tell a bound view to refresh — and promptly: SetDependencyUse itself (not just
    // the async disk work it kicks off afterward, which can take a moment) raises it synchronously, so a
    // bound row updates the instant the checkbox is toggled.
    [Fact]
    public async Task Pill_raises_property_changed_synchronously_when_the_skip_state_changes()
    {
        var a = Dep("a", "A", optional: true);
        var (f, vm) = await Open(Photo(a));
        var item = Item(vm);
        var seen = new List<string>();
        item.PropertyChanged += (_, e) => { if (e.PropertyName is { } n) seen.Add(n); };

        vm.SetDependencyUse(item, "a", use: false);

        Assert.Contains(nameof(PluginItemViewModel.ExtrasPillText), seen);   // before awaiting anything
        await vm.DependencyWork;
    }

    [Fact]
    public async Task Pill_click_navigates_to_the_detail_page_and_signals_a_scroll_to_dependencies()
    {
        var a = Dep("a", "A", optional: true);
        var (f, vm) = await Open(Photo(a));
        var row = Row(vm);
        var scrolled = 0;
        vm.ScrollToDependenciesRequested += () => scrolled++;

        await row.OpenDependenciesCommand.ExecuteAsync(null);

        Assert.True(vm.IsDetailOpen);
        Assert.Same(row.Item, vm.SelectedPlugin);
        Assert.Equal(1, scrolled);
    }
}

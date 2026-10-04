using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels.Dashboard;
using StellarLauncher.App.ViewModels.Workspace;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using Xunit;

/// <summary>v3 V1/V2 (spec § 12) end-to-end on the real dependency service: every install path asks first when the
/// version declares optional dependencies; an unticked one is recorded BEFORE the ensure and never downloaded; a
/// reinstall can re-download dependencies without overwriting the player's file; Vanilla defers.</summary>
public class InstallStepTests
{
    private const string G = "/opt/game/X/drive_c/Star/StarLauncher/game/release_3.7/game_mini";

    private static PluginDependency Dep(string id, string name, bool optional, params string[] requires) =>
        new(id, name, "1.0.0", $"https://cdn/deps/{id}", WorkspaceFixture.DllSha, WorkspaceFixture.DllBytes.Length, "file",
            new[] { new PluginDependencyFile(null, $"{id}.bin") }, "game", ModdedOnly: true, Optional: optional,
            Requires: requires.Length == 0 ? null : requires, License: "MIT", LicenseUrl: "https://l", SourceUrl: "https://s",
            Description: $"{name} does things.");

    private static readonly PluginDependency Fx = Dep("fx", "Effects runtime", optional: true);
    private static readonly PluginDependency Bridge = Dep("bridge", "Effects bridge", optional: false, "fx");

    private static PluginVersion V(string version, params PluginDependency[] deps) =>
        new(version, null, "Stellar.Photo.dll", $"https://cdn/photo/{version}.dll", WorkspaceFixture.DllSha, "2.0.0", null, null, Dependencies: deps);

    private static async Task<(WorkspaceFixture f, ClientPluginsViewModel vm)> Open(PluginEntry entry, string? installed = null,
        bool modded = true, Func<IDependencyService, IDependencyService>? wrap = null)
    {
        var f = new WorkspaceFixture { WrapDependencies = wrap };
        f.Registry.Add(entry);
        f.AddClient("c1", "Main", G, framework: "2.8.0");
        if (!modded) { var cfg = f.Store.Load(); cfg.Clients.Single().Modded = false; f.Store.Save(cfg); }
        if (installed is not null) f.InstallPlugin(G, entry.Id, "Stellar.Photo.dll", installed);
        f.Tabs = f.Tabs with { Plugins = w => new ClientPluginsViewModel(w) };
        f.Start();
        var ws = await f.OpenAsync("c1");
        ws.ShowPluginsCommand.Execute(null);
        var vm = (ClientPluginsViewModel)ws.TabContent!;
        await vm.ReloadAsync();
        return (f, vm);
    }

    private static PluginEntry Photo(params PluginVersion[] versions) => new("photo", "Photo Thing", "d", "StellarProtocol", versions);
    private static int Fetches(WorkspaceFixture f, string id) { lock (f.Requested) return f.Requested.Count(u => u == $"https://cdn/deps/{id}"); }
    private static PluginRowViewModel Row(ClientPluginsViewModel vm) => vm.Rows.Single(r => r.Item.Entry.Id == "photo");

    [Fact]
    public async Task Installing_asks_first_and_installs_everything_ticked_by_default()
    {
        var (f, vm) = await Open(Photo(V("1.5.0", Fx, Bridge)));
        await Row(vm).InstallCommand.ExecuteAsync(null);

        var step = Assert.IsType<InstallStep>(Assert.Single(f.Steps.Asked));
        Assert.Equal("Effects runtime 1.0.0 + Effects bridge", Assert.Single(step.Options).Title);
        Assert.True(f.Fs.File.Exists($"{G}/stellar/plugins/photo/Stellar.Photo.dll"));
        Assert.True(f.Fs.File.Exists($"{G}/fx.bin"));
        Assert.True(f.Fs.File.Exists($"{G}/bridge.bin"));
    }

    // Review fix round 2 (b): a returning player's earlier opt-out (from a prior session's config) reads back as
    // unticked when the install step reopens, instead of defaulting to ticked again.
    [Fact]
    public async Task A_fresh_install_step_pre_unticks_a_previously_skipped_dependency()
    {
        var (f, vm) = await Open(Photo(V("1.5.0", Fx, Bridge)));
        f.Shell.Config.Clients.Single().SkippedDependencies.Add("photo/fx");   // carried over from an earlier session

        await Row(vm).InstallCommand.ExecuteAsync(null);

        var step = Assert.IsType<InstallStep>(Assert.Single(f.Steps.Asked));
        Assert.True(Assert.Single(step.Options).InitiallyUnticked);
    }

    private sealed class SkipsAtEnsure(IDependencyService inner, WorkspaceFixture f) : ForwardingDependencyService(inner)
    {
        public readonly List<string[]> SavedAtEnsure = new();
        public override Task<IReadOnlyList<DependencyStatus>> EnsureAsync(string g, string p, IReadOnlyList<PluginDependency> d, ISet<string> s, CancellationToken ct)
        {
            SavedAtEnsure.Add(f.Store.Load().Clients.Single().SkippedDependencies.ToArray());
            return base.EnsureAsync(g, p, d, s, ct);
        }
    }

    [Fact]
    public async Task An_unticked_dependency_is_saved_before_the_ensure_and_never_downloaded()
    {
        // Built by hand (not Open): the spy needs the fixture itself, to read what is SAVED at ensure time.
        SkipsAtEnsure? spy = null;
        var f = new WorkspaceFixture();
        f.WrapDependencies = inner => spy = new SkipsAtEnsure(inner, f);
        f.Registry.Add(Photo(V("1.5.0", Fx, Bridge)));
        f.AddClient("c1", "Main", G, framework: "2.8.0");
        f.Tabs = f.Tabs with { Plugins = w => new ClientPluginsViewModel(w) };
        f.Start();
        var ws = await f.OpenAsync("c1");
        ws.ShowPluginsCommand.Execute(null);
        var vm = (ClientPluginsViewModel)ws.TabContent!;
        await vm.ReloadAsync();
        f.Steps.Install = _ => new InstallStepResult(new HashSet<string> { "fx" });

        await Row(vm).InstallCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "photo/fx" }, Assert.Single(spy!.SavedAtEnsure));   // persisted BEFORE the ensure ran
        Assert.Equal(0, Fetches(f, "fx"));
        Assert.Equal(0, Fetches(f, "bridge"));                                     // its dependent is skipped with it
        Assert.False(f.Fs.File.Exists($"{G}/fx.bin"));
        Assert.True(f.Fs.File.Exists($"{G}/stellar/plugins/photo/Stellar.Photo.dll"));
    }

    [Fact]
    public async Task Cancelling_the_install_step_downloads_and_records_nothing()
    {
        var (f, vm) = await Open(Photo(V("1.5.0", Fx, Bridge)));
        f.Steps.Install = _ => null;

        await Row(vm).InstallCommand.ExecuteAsync(null);

        Assert.Empty(f.Requested);
        Assert.False(f.Fs.File.Exists($"{G}/stellar/plugins/photo/Stellar.Photo.dll"));
        Assert.Empty(f.Store.Load().Clients.Single().SkippedDependencies);
    }

    [Fact]
    public async Task A_plugin_without_optional_dependencies_installs_in_one_click()
    {
        var (f, vm) = await Open(Photo(V("1.0.0", Dep("req", "Required thing", optional: false))));
        await Row(vm).InstallCommand.ExecuteAsync(null);
        Assert.Empty(f.Steps.Asked);
        Assert.True(f.Fs.File.Exists($"{G}/req.bin"));
    }

    [Fact]
    public async Task An_update_asks_only_about_a_new_optional_dependency()
    {
        var lut = Dep("lut", "Colour tables", optional: true);
        var (f, vm) = await Open(Photo(V("2.0.0", Fx, Bridge, lut), V("1.5.0", Fx, Bridge)), installed: "1.5.0");

        await Row(vm).UpdateCommand.ExecuteAsync(null);

        var step = Assert.IsType<InstallStep>(Assert.Single(f.Steps.Asked));
        Assert.Equal(new[] { "lut" }, step.Options.Select(o => o.DependencyId));
    }

    [Fact]
    public async Task Install_from_the_dashboard_goes_through_the_install_step()
    {
        var f = new WorkspaceFixture();
        f.Registry.Add(Photo(V("1.5.0", Fx, Bridge)));
        f.AddClient("c1", "Main", G, framework: "2.8.0");
        f.Start();
        var dash = (DashboardViewModel)f.Shell.Current!;
        await dash.RefreshAsync();
        dash.ShowAllRows = true;   // a plugin installed nowhere is not in the default (present-only) rows
        f.Steps.Install = _ => new InstallStepResult(new HashSet<string> { "fx" });

        await dash.Rows.Single(r => r.PluginId == "photo").Cells.Single().ClickCommand.ExecuteAsync(null);

        Assert.IsType<InstallStep>(Assert.Single(f.Steps.Asked));
        Assert.Equal(new[] { "photo/fx" }, f.Store.Load().Clients.Single().SkippedDependencies);
        Assert.True(f.Fs.File.Exists($"{G}/stellar/plugins/photo/Stellar.Photo.dll"));
        Assert.False(f.Fs.File.Exists($"{G}/fx.bin"));
    }

    // Review fix round 2 (d): Cancel through the dashboard path changes nothing either — the same guarantee
    // already pinned for the plugins-tab row (Cancelling_the_install_step_downloads_and_records_nothing).
    [Fact]
    public async Task Cancelling_the_install_step_from_the_dashboard_downloads_and_records_nothing()
    {
        var f = new WorkspaceFixture();
        f.Registry.Add(Photo(V("1.5.0", Fx, Bridge)));
        f.AddClient("c1", "Main", G, framework: "2.8.0");
        f.Start();
        var dash = (DashboardViewModel)f.Shell.Current!;
        await dash.RefreshAsync();
        dash.ShowAllRows = true;
        f.Steps.Install = _ => null;

        await dash.Rows.Single(r => r.PluginId == "photo").Cells.Single().ClickCommand.ExecuteAsync(null);

        Assert.Empty(f.Requested);
        Assert.False(f.Fs.File.Exists($"{G}/stellar/plugins/photo/Stellar.Photo.dll"));
        Assert.Empty(f.Store.Load().Clients.Single().SkippedDependencies);
    }

    // Review fix round 2 (d): an update started from the dashboard matrix offers only a NEW optional dependency,
    // the same guarantee already pinned for the plugins-tab row (An_update_asks_only_about_a_new_optional_dependency).
    [Fact]
    public async Task A_dashboard_update_asks_only_about_a_new_optional_dependency()
    {
        var lut = Dep("lut", "Colour tables", optional: true);
        var f = new WorkspaceFixture();
        f.Registry.Add(Photo(V("2.0.0", Fx, Bridge, lut), V("1.5.0", Fx, Bridge)));
        f.AddClient("c1", "Main", G, framework: "2.8.0");
        f.InstallPlugin(G, "photo", "Stellar.Photo.dll", "1.5.0");
        f.Start();
        var dash = (DashboardViewModel)f.Shell.Current!;
        await dash.RefreshAsync();
        f.Steps.Install = _ => new InstallStepResult(new HashSet<string>());

        await dash.Rows.Single(r => r.PluginId == "photo").Cells.Single().ClickCommand.ExecuteAsync(null);

        var step = Assert.IsType<InstallStep>(Assert.Single(f.Steps.Asked));
        Assert.Equal(new[] { "lut" }, step.Options.Select(o => o.DependencyId));
    }

    [Fact]
    public async Task The_plugin_page_opens_the_step_instead_of_its_inline_confirm()
    {
        var (f, vm) = await Open(Photo(V("1.5.0", Fx, Bridge)));
        var item = Row(vm).Item;

        await item.RequestInstallCommand.ExecuteAsync(null);

        Assert.False(item.ConfirmVisible);
        Assert.Single(f.Steps.Asked);
        Assert.True(f.Fs.File.Exists($"{G}/stellar/plugins/photo/Stellar.Photo.dll"));
    }

    [Fact]
    public async Task Without_a_step_the_plugin_page_keeps_its_inline_confirm()
    {
        var (f, vm) = await Open(Photo(V("1.0.0")));
        var item = Row(vm).Item;
        await item.RequestInstallCommand.ExecuteAsync(null);
        Assert.True(item.ConfirmVisible);
        Assert.Empty(f.Steps.Asked);
    }

    [Fact]
    public async Task Reinstall_keeps_dependencies_by_default_and_redownloads_them_when_ticked()
    {
        var (f, vm) = await Open(Photo(V("1.5.0", Fx, Bridge)), installed: "1.5.0");
        await f.Services.Core.Install.Dependencies.EnsureAsync(G, "photo", new[] { Fx, Bridge }, new HashSet<string>(), CancellationToken.None);
        var item = Row(vm).Item;

        await item.RequestInstallCommand.ExecuteAsync(null);                  // default: plugin only
        Assert.IsType<ReinstallStep>(Assert.Single(f.Steps.Asked));
        Assert.Equal(1, Fetches(f, "fx"));

        f.Steps.Reinstall = _ => true;
        await vm.ReloadAsync();
        await Row(vm).Item.RequestInstallCommand.ExecuteAsync(null);
        Assert.Equal(2, Fetches(f, "fx"));
        Assert.Equal(2, Fetches(f, "bridge"));
        Assert.True(f.Fs.File.Exists($"{G}/fx.bin"));
    }

    [Fact]
    public async Task Reinstalling_dependencies_never_overwrites_a_file_the_player_changed()
    {
        var (f, vm) = await Open(Photo(V("1.5.0", Fx, Bridge)), installed: "1.5.0");
        await f.Services.Core.Install.Dependencies.EnsureAsync(G, "photo", new[] { Fx, Bridge }, new HashSet<string>(), CancellationToken.None);
        f.Fs.File.WriteAllText($"{G}/fx.bin", "the player's own build");
        f.Steps.Reinstall = _ => true;

        await Row(vm).Item.RequestInstallCommand.ExecuteAsync(null);

        Assert.Equal("the player's own build", f.Fs.File.ReadAllText($"{G}/fx.bin"));
    }

    [Fact]
    public async Task A_vanilla_reinstall_defers_the_dependency_part_to_the_next_modded_launch()
    {
        var (f, vm) = await Open(Photo(V("1.5.0", Fx, Bridge)), installed: "1.5.0", modded: false);
        var svc = f.Services.Core.Install.Dependencies;
        await svc.EnsureAsync(G, "photo", new[] { Fx, Bridge }, new HashSet<string>(), CancellationToken.None);
        f.Steps.Reinstall = _ => true;

        await Row(vm).Item.RequestInstallCommand.ExecuteAsync(null);

        Assert.Equal(1, Fetches(f, "fx"));                                    // nothing yet
        Assert.Contains("dependencies will be reinstalled at next modded launch", vm.Status);
        await svc.EnsureAsync(G, "photo", new[] { Fx, Bridge }, new HashSet<string>(), CancellationToken.None);   // the Modded launch's ensure
        Assert.Equal(2, Fetches(f, "fx"));
    }

    [Fact]
    public async Task Cancelling_the_reinstall_step_changes_nothing()
    {
        var (f, vm) = await Open(Photo(V("1.5.0", Fx, Bridge)), installed: "1.5.0");
        f.Steps.Reinstall = _ => null;
        await Row(vm).Item.RequestInstallCommand.ExecuteAsync(null);
        Assert.Empty(f.Requested);
    }

    // Review fix round 1 (c): a reinstall whose every dependency is skipped must need no step — the page falls back
    // to its inline Confirm (exactly like a plugin with no optional dependencies at all), and confirming installs
    // with nothing asked and the skipped dependency still never downloaded.
    [Fact]
    public async Task Reinstalling_with_every_dependency_skipped_needs_no_step()
    {
        var (f, vm) = await Open(Photo(V("1.5.0", Fx)), installed: "1.5.0");
        // Mutate the LIVE client object (the one the already-open workspace holds), not a disconnected
        // Store.Load() copy — Config is read once at Shell.Start() and kept in memory thereafter.
        f.Shell.Config.Clients.Single().SkippedDependencies.Add("photo/fx");
        var item = Row(vm).Item;

        await item.RequestInstallCommand.ExecuteAsync(null);
        Assert.True(item.ConfirmVisible);   // no step opened — HasInstallStep said no
        await item.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Empty(f.Steps.Asked);
        Assert.Equal(0, Fetches(f, "fx"));
    }
}

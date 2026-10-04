using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels;
using StellarLauncher.App.ViewModels.Workspace;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using Xunit;

/// <summary>v3 V3 (spec § 12): removing a plugin with dependencies asks — remove them too (default) or keep them under
/// launcher management; kept ones show on the page with "Remove kept dependencies", and a reinstall adopts them.</summary>
public class RemoveStepTests
{
    private const string G = "/opt/game/X/drive_c/Star/StarLauncher/game/release_3.7/game_mini";

    private static PluginDependency Dep(string id, string name, bool optional, params string[] requires) =>
        new(id, name, "1.0.0", $"https://cdn/deps/{id}", WorkspaceFixture.DllSha, WorkspaceFixture.DllBytes.Length, "file",
            new[] { new PluginDependencyFile(null, $"{id}.bin") }, "game", ModdedOnly: true, Optional: optional,
            Requires: requires.Length == 0 ? null : requires, License: "MIT", LicenseUrl: "https://l", SourceUrl: "https://s");

    private static readonly PluginDependency Fx = Dep("fx", "Effects runtime", optional: true);
    private static readonly PluginDependency Bridge = Dep("bridge", "Effects bridge", optional: false, "fx");

    private static PluginVersion V(string version, params PluginDependency[] deps) =>
        new(version, null, "Stellar.Photo.dll", $"https://cdn/photo/{version}.dll", WorkspaceFixture.DllSha, "2.0.0", null, null, Dependencies: deps);

    private static async Task<(WorkspaceFixture f, ClientPluginsViewModel vm)> Open(PluginEntry entry, bool ensure = true,
        Func<IDependencyService, IDependencyService>? wrap = null)
    {
        var f = new WorkspaceFixture { WrapDependencies = wrap };
        f.Registry.Add(entry);
        f.AddClient("c1", "Main", G, framework: "2.8.0");
        f.InstallPlugin(G, entry.Id, "Stellar.Photo.dll", entry.Versions.Last().Version);
        f.Tabs = f.Tabs with { Plugins = w => new ClientPluginsViewModel(w) };
        f.Start();
        if (ensure)
            await f.Services.Core.Install.Dependencies.EnsureAsync(G, entry.Id, entry.Versions.Last().Dependencies!, new HashSet<string>(), CancellationToken.None);
        var ws = await f.OpenAsync("c1");
        ws.ShowPluginsCommand.Execute(null);
        var vm = (ClientPluginsViewModel)ws.TabContent!;
        await vm.ReloadAsync();
        return (f, vm);
    }

    private static PluginEntry Photo(params PluginVersion[] versions) => new("photo", "Photo Thing", "d", "StellarProtocol", versions);
    private static PluginItemViewModel Item(ClientPluginsViewModel vm) => vm.Rows.Single(r => r.Item.Entry.Id == "photo").Item;
    private static string Dll => $"{G}/stellar/plugins/photo/Stellar.Photo.dll";

    [Fact]
    public async Task Without_a_ledger_remove_is_one_click_as_before()
    {
        var (f, vm) = await Open(Photo(V("1.0.0", Fx, Bridge)), ensure: false);
        await Item(vm).RemoveCommand.ExecuteAsync(null);
        Assert.Empty(f.Steps.Asked);
        Assert.False(f.Fs.File.Exists(Dll));
    }

    // Review fix round 1 (a): a ledger with entries the CURRENT version no longer declares (e.g. a dropped
    // dependency not yet swept by an ensure) must not open a step either — PluginStepBuilder.Remove's "the
    // dependencies" placeholder is for an all-skipped-but-declared ledger, never for nothing declared at all.
    [Fact]
    public async Task A_ledger_the_current_version_declares_nothing_from_is_also_one_click()
    {
        var (f, vm) = await Open(Photo(V("1.0.0")), ensure: false);   // "1.0.0" declares NO dependencies
        // Simulate a stale ledger left behind by a dropped dependency (not yet cleaned by an ensure).
        await f.Services.Core.Install.Dependencies.EnsureAsync(G, "photo", new[] { Fx }, new HashSet<string>(), CancellationToken.None);
        await vm.ReloadAsync();

        await Item(vm).RemoveCommand.ExecuteAsync(null);

        Assert.Empty(f.Steps.Asked);
        Assert.False(f.Fs.File.Exists(Dll));
        Assert.False(f.Fs.File.Exists($"{G}/fx.bin"));   // still cleaned up via the default (plugin+deps) path
    }

    [Fact]
    public async Task The_default_removes_the_plugin_and_its_dependencies()
    {
        var (f, vm) = await Open(Photo(V("1.0.0", Fx, Bridge)));
        await Item(vm).RemoveCommand.ExecuteAsync(null);

        var step = Assert.IsType<RemoveStep>(Assert.Single(f.Steps.Asked));
        Assert.Equal(new[] { "Effects runtime", "Effects bridge" }, step.DependencyNames);
        Assert.False(f.Fs.File.Exists(Dll));
        Assert.False(f.Fs.File.Exists($"{G}/fx.bin"));
        Assert.False(f.Fs.File.Exists($"{G}/stellar/deps/photo.json"));
    }

    [Fact]
    public async Task Cancelling_the_remove_step_removes_nothing()
    {
        var (f, vm) = await Open(Photo(V("1.0.0", Fx, Bridge)));
        f.Steps.Remove = _ => null;
        await Item(vm).RemoveCommand.ExecuteAsync(null);
        Assert.True(f.Fs.File.Exists(Dll));
        Assert.True(f.Fs.File.Exists($"{G}/fx.bin"));
        Assert.True(f.Fs.File.Exists($"{G}/stellar/deps/photo.json"));
    }

    [Fact]
    public async Task Plugin_only_keeps_the_dependencies_under_launcher_management()
    {
        var (f, vm) = await Open(Photo(V("1.0.0", Fx, Bridge)));
        var cfg = f.Store.Load(); cfg.Clients.Single().SkippedDependencies.Add("photo/x"); f.Store.Save(cfg);
        f.Shell.Config.Clients.Single().SkippedDependencies.Add("photo/x");
        f.Steps.Remove = _ => RemoveChoice.PluginOnly;

        await Item(vm).RemoveCommand.ExecuteAsync(null);

        Assert.False(f.Fs.File.Exists(Dll));
        Assert.True(f.Fs.File.Exists($"{G}/fx.bin"));
        Assert.True(f.Fs.File.Exists($"{G}/bridge.bin"));
        Assert.True(f.Services.Core.Install.Dependencies.IsKept(G, "photo"));
        Assert.Empty(f.Store.Load().Clients.Single().SkippedDependencies);
        Assert.Contains("kept", vm.Status);
    }

    private sealed class KeepFails(IDependencyService inner) : ForwardingDependencyService(inner)
    {
        public override Task SetKeptAsync(string g, string p, bool kept, CancellationToken ct = default) =>
            kept ? throw new InvalidOperationException("dependency record could not be read") : base.SetKeptAsync(g, p, kept, ct);
    }

    [Fact]
    public async Task If_the_dependencies_cannot_be_kept_nothing_is_removed()
    {
        var (f, vm) = await Open(Photo(V("1.0.0", Fx, Bridge)), wrap: inner => new KeepFails(inner));
        f.Steps.Remove = _ => RemoveChoice.PluginOnly;

        await Item(vm).RemoveCommand.ExecuteAsync(null);

        Assert.True(f.Fs.File.Exists(Dll));
        Assert.True(f.Fs.File.Exists($"{G}/fx.bin"));
        Assert.Contains("nothing was removed", vm.Status);
    }

    [Fact]
    public async Task The_page_shows_kept_dependencies_and_removes_them_on_request()
    {
        var (f, vm) = await Open(Photo(V("1.0.0", Fx, Bridge)));
        f.Steps.Remove = _ => RemoveChoice.PluginOnly;
        await Item(vm).RemoveCommand.ExecuteAsync(null);
        await vm.ReloadAsync();
        var item = Item(vm);

        vm.OpenPluginCommand.Execute(item);
        await item.DependencyRefresh;

        Assert.True(item.DependenciesKept);
        Assert.True(item.ShowDependencySection);
        Assert.Equal("Kept after Photo Thing was removed. Still moved aside for Vanilla launches; reinstalling Photo Thing uses it again.", item.KeptNote);
        Assert.All(item.Dependencies, d => Assert.Equal("Installed", d.StateText));

        await item.RemoveKeptDependenciesCommand.ExecuteAsync(null);
        await item.DependencyRefresh;

        Assert.False(f.Fs.File.Exists($"{G}/fx.bin"));
        Assert.False(f.Fs.File.Exists($"{G}/stellar/deps/photo.json"));
        Assert.False(item.DependenciesKept);
    }

    [Fact]
    public async Task Removing_kept_dependencies_waits_while_the_game_runs()
    {
        var (f, vm) = await Open(Photo(V("1.0.0", Fx, Bridge)));
        f.Steps.Remove = _ => RemoveChoice.PluginOnly;
        await Item(vm).RemoveCommand.ExecuteAsync(null);
        await vm.ReloadAsync();
        var item = Item(vm);
        f.Shell.Sessions.For(f.Shell.Config.Clients.Single()).Begin(DateTimeOffset.UnixEpoch);   // Launching → IsBusy

        await item.RemoveKeptDependenciesCommand.ExecuteAsync(null);

        Assert.True(f.Fs.File.Exists($"{G}/fx.bin"));
        Assert.Contains("close the game", vm.Status);
    }

    [Fact]
    public async Task Reinstalling_the_plugin_adopts_its_kept_dependencies_without_downloading_them()
    {
        var (f, vm) = await Open(Photo(V("1.0.0", Fx, Bridge)));
        f.Steps.Remove = _ => RemoveChoice.PluginOnly;
        await Item(vm).RemoveCommand.ExecuteAsync(null);
        await vm.ReloadAsync();
        var before = f.Requested.Count(u => u.Contains("/deps/"));

        await vm.Rows.Single(r => r.Item.Entry.Id == "photo").InstallCommand.ExecuteAsync(null);

        Assert.True(f.Fs.File.Exists(Dll));
        Assert.False(f.Services.Core.Install.Dependencies.IsKept(G, "photo"));
        Assert.Equal(before, f.Requested.Count(u => u.Contains("/deps/")));
    }

    // Controller brief: a reinstall whose version declares different dependencies — the dropped-dependency cleanup applies once adopted.
    [Fact]
    public async Task Reinstalling_a_version_that_dropped_a_kept_dependency_removes_it()
    {
        var lut = Dep("lut", "Colour tables", optional: true);
        var (f, vm) = await Open(Photo(V("1.6.0", Fx, Bridge), V("1.5.0", Fx, Bridge, lut)));   // 1.5.0 installed + ensured
        f.Steps.Remove = _ => RemoveChoice.PluginOnly;
        await Item(vm).RemoveCommand.ExecuteAsync(null);
        await vm.ReloadAsync();

        await vm.Rows.Single(r => r.Item.Entry.Id == "photo").InstallCommand.ExecuteAsync(null);   // installs 1.6.0

        Assert.False(f.Fs.File.Exists($"{G}/lut.bin"));
        Assert.True(f.Fs.File.Exists($"{G}/fx.bin"));
    }
}

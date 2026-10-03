using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using StellarLauncher.App.ViewModels.Workspace;
using StellarLauncher.Core.Dependencies;
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

    // Controller round: removing a plugin also clears whatever its dependencies placed (the ledger +
    // the files it tracked) and this client's "<id>/<dep>" skip choices for it, then persists — a stale
    // ledger/skip entry for a plugin that no longer exists would otherwise confuse the next install.
    [Fact]
    public async Task Remove_also_clears_its_dependencies_and_skipped_list_and_persists()
    {
        var (f, vm) = await Open();
        var client = f.Shell.Config.Clients.Single(c => c.Id == "c2");
        client.SkippedDependencies = new List<string> { "combatmeter/shaderpack", "playerhud/x" };
        f.Store.Save(f.Shell.Config);

        // Seed a dependency ledger for combatmeter, as if an earlier EnsureAsync had placed a file.
        new DependencyLedgerStore(f.Fs).Write(Test, new DependencyLedger("combatmeter", new[]
        {
            new LedgerEntry("shaderpack", "1.0", new[] { new LedgerFile("stellar/deps/combatmeter/shader.pak", Convert.ToHexString(SHA256.HashData(new byte[] { 1 })), false) }),
        }));
        f.Fs.AddFile($"{Test}/stellar/deps/combatmeter/shader.pak", new MockFileData(new byte[] { 1 }));

        var cm = vm.Rows.Single(r => r.Item.Entry.Id == "combatmeter");
        await cm.Item.RemoveCommand.ExecuteAsync(null);

        Assert.False(f.Fs.File.Exists($"{Test}/stellar/deps/combatmeter.json"));
        Assert.False(f.Fs.File.Exists($"{Test}/stellar/deps/combatmeter/shader.pak"));
        var updated = f.Store.Load().Clients.Single(c => c.Id == "c2");
        Assert.Equal(new[] { "playerhud/x" }, updated.SkippedDependencies);
    }

    // Fix round 1, Minor 2: records whether the plugin's own DLL still existed on disk at the moment
    // RemoveAll ran, proving the call ORDER (dependency cleanup before plugin removal) without needing to
    // fake IPluginInstaller too.
    private sealed class OrderTrackingDependencyService : IDependencyService
    {
        private readonly System.IO.Abstractions.IFileSystem _fs;
        private readonly string _dllPath;
        public bool ThrowOnRemoveAll;
        public int RemoveAllCalls;
        public bool DllStillPresentWhenRemoveAllRan;

        public OrderTrackingDependencyService(System.IO.Abstractions.IFileSystem fs, string dllPath) { _fs = fs; _dllPath = dllPath; }

        public Task<IReadOnlyList<DependencyStatus>> EnsureAsync(string gameMini, string pluginId,
            IReadOnlyList<StellarLauncher.Core.Model.PluginDependency> deps, ISet<string> skippedIds, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DependencyStatus>>(Array.Empty<DependencyStatus>());
        public IReadOnlyList<DependencyStatus> Status(string gameMini, string pluginId,
            IReadOnlyList<StellarLauncher.Core.Model.PluginDependency> deps, ISet<string> skippedIds) => Array.Empty<DependencyStatus>();
        public Task RemoveAsync(string gameMini, string pluginId, string dependencyId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAllAsync(string gameMini, string pluginId, CancellationToken ct = default)
        {
            RemoveAllCalls++;
            DllStillPresentWhenRemoveAllRan = _fs.File.Exists(_dllPath);
            if (ThrowOnRemoveAll) throw new InvalidOperationException("ledger busy");
            return Task.CompletedTask;
        }
        public Task ParkModdedOnlyAsync(string gameMini, CancellationToken ct = default) => Task.CompletedTask;
        public Task UnparkModdedOnlyAsync(string gameMini, CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<string> LedgerPluginIds(string gameMini) => Array.Empty<string>();
    }

    [Fact]
    public async Task Remove_runs_dependency_cleanup_before_plugin_removal_and_keeps_skip_entries_if_cleanup_fails()
    {
        var f = new WorkspaceFixture();
        f.Registry.Add(WorkspaceFixture.Entry("combatmeter", "CombatMeter", "2.0.0", "2.10.0"));
        f.AddClient("c2", "Test", Test, channel: "testing", framework: "2.7.4", accent: "#ffb347");
        f.InstallPlugin(Test, "combatmeter", "Stellar.CombatMeter.dll", "2.10.0");

        var seeded = f.Store.Load();
        seeded.Clients.Single(c => c.Id == "c2").SkippedDependencies.Add("combatmeter/shaderpack");
        f.Store.Save(seeded);

        var dllPath = $"{Test}/stellar/plugins/combatmeter/Stellar.CombatMeter.dll";
        var fake = new OrderTrackingDependencyService(f.Fs, dllPath) { ThrowOnRemoveAll = true };
        f.Dependencies = fake;
        f.Tabs = f.Tabs with { Plugins = w => new ClientPluginsViewModel(w) };
        f.Start();
        var ws = await f.OpenAsync("c2");
        ws.ShowPluginsCommand.Execute(null);
        var vm = (ClientPluginsViewModel)ws.TabContent!;
        await vm.ReloadAsync();

        var cm = vm.Rows.Single(r => r.Item.Entry.Id == "combatmeter");
        await cm.Item.RemoveCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.RemoveAllCalls);
        Assert.True(fake.DllStillPresentWhenRemoveAllRan); // RemoveAll ran BEFORE Plugins.Remove deleted the dll
        Assert.False(f.Fs.File.Exists(dllPath));           // the plugin was still removed despite the cleanup failure
        var updated = f.Store.Load().Clients.Single(c => c.Id == "c2");
        Assert.Equal(new[] { "combatmeter/shaderpack" }, updated.SkippedDependencies); // kept — cleanup never confirmed done
        Assert.Contains("removed", vm.Status, StringComparison.OrdinalIgnoreCase);
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

    // ---- Task 6: plugin page DEPENDENCIES section ----

    private static StellarLauncher.Core.Model.PluginDependency DepCase(string id, bool optional, params string[] requires) =>
        new(id, $"{id} name", "1.0", $"https://cdn/deps/{id}", WorkspaceFixture.DllSha,
            WorkspaceFixture.DllBytes.Length, "file", new[] { new StellarLauncher.Core.Model.PluginDependencyFile(null, $"{id}.bin") },
            "plugin", Optional: optional, Requires: requires.Length == 0 ? null : requires, License: "MIT", LicenseUrl: "https://l", SourceUrl: "https://s");

    /// <summary>One installed plugin ("photo", v1.0.0) on client c2 declaring opt (optional), withopt
    /// (required, requires opt) and req (required).</summary>
    private static async Task<(WorkspaceFixture f, ClientPluginsViewModel vm, StellarLauncher.App.ViewModels.PluginItemViewModel item)> OpenWithDeps(bool modded = true)
    {
        var f = new WorkspaceFixture();
        var deps = new[] { DepCase("opt", true), DepCase("withopt", false, "opt"), DepCase("req", false) };
        f.Registry.Add(new StellarLauncher.Core.Model.PluginEntry("photo", "Photo", "d", "a", new[]
        {
            new StellarLauncher.Core.Model.PluginVersion("1.0.0", null, "Stellar.Photo.dll", "https://cdn/photo/1.0.0.dll",
                WorkspaceFixture.DllSha, "2.0.0", null, null, Dependencies: deps),
        }));
        f.AddClient("c2", "Test", Test, framework: "2.7.4");
        f.InstallPlugin(Test, "photo", "Stellar.Photo.dll", "1.0.0");
        var seeded = f.Store.Load(); seeded.Clients.Single(c => c.Id == "c2").Modded = modded; f.Store.Save(seeded);
        f.Tabs = f.Tabs with { Plugins = w => new ClientPluginsViewModel(w) };
        f.Start();
        var ws = await f.OpenAsync("c2");
        ws.ShowPluginsCommand.Execute(null);
        var vm = (ClientPluginsViewModel)ws.TabContent!;
        await vm.ReloadAsync();
        var item = vm.Rows.Single().Item;
        // Everything installed, as after a Modded launch.
        await f.Services.Core.Install.Dependencies.EnsureAsync(Test, "photo", deps, new HashSet<string>(), CancellationToken.None);
        vm.OpenPluginCommand.Execute(item);
        await item.DependencyRefresh;
        return (f, vm, item);
    }

    [Fact]
    public async Task Opening_the_page_lists_the_dependencies_with_their_state()
    {
        var (_, _, item) = await OpenWithDeps();

        Assert.True(item.HasDependencies);
        Assert.Equal(new[] { "opt", "withopt", "req" }, item.Dependencies.Select(d => d.Id));
        Assert.All(item.Dependencies, d => Assert.Equal("Installed", d.StateText));
        Assert.All(item.Dependencies, d => Assert.True(d.Use));
    }

    [Fact]
    public async Task Unticking_an_optional_dependency_saves_the_skip_and_removes_it_and_its_dependents_now()
    {
        var (f, vm, item) = await OpenWithDeps();

        item.Dependencies.Single(d => d.Id == "opt").Use = false;
        await vm.DependencyWork;

        Assert.Equal(new[] { "photo/opt" }, f.Store.Load().Clients.Single(c => c.Id == "c2").SkippedDependencies);
        Assert.False(f.Fs.File.Exists($"{Test}/stellar/deps/photo/opt.bin"));
        Assert.False(f.Fs.File.Exists($"{Test}/stellar/deps/photo/withopt.bin"));   // requires opt
        Assert.True(f.Fs.File.Exists($"{Test}/stellar/deps/photo/req.bin"));
        Assert.Equal("Skipped", item.Dependencies.Single(d => d.Id == "opt").StateText);
        Assert.Equal("Skipped", item.Dependencies.Single(d => d.Id == "withopt").StateText);
        Assert.Equal("Installed", item.Dependencies.Single(d => d.Id == "req").StateText);
        Assert.Contains("skipped", vm.Status);
    }

    // Task 6 (e): only optional dependencies can be skipped.
    [Fact]
    public async Task A_required_dependency_cannot_be_skipped()
    {
        var (f, vm, item) = await OpenWithDeps();

        vm.SetDependencyUse(item, "req", false);
        item.Dependencies.Single(d => d.Id == "withopt").Use = false;
        await vm.DependencyWork;

        Assert.Empty(f.Store.Load().Clients.Single(c => c.Id == "c2").SkippedDependencies);
        Assert.True(f.Fs.File.Exists($"{Test}/stellar/deps/photo/req.bin"));
        Assert.True(f.Fs.File.Exists($"{Test}/stellar/deps/photo/withopt.bin"));
    }

    [Fact]
    public async Task Ticking_it_again_on_a_Modded_client_installs_it_in_the_background()
    {
        var (f, vm, item) = await OpenWithDeps();
        item.Dependencies.Single(d => d.Id == "opt").Use = false;
        await vm.DependencyWork;

        item.Dependencies.Single(d => d.Id == "opt").Use = true;
        await vm.DependencyWork;

        Assert.Empty(f.Store.Load().Clients.Single(c => c.Id == "c2").SkippedDependencies);
        Assert.True(f.Fs.File.Exists($"{Test}/stellar/deps/photo/opt.bin"));
        Assert.True(f.Fs.File.Exists($"{Test}/stellar/deps/photo/withopt.bin"));
        Assert.All(item.Dependencies, d => Assert.Equal("Installed", d.StateText));
        Assert.Equal("Photo: opt name installed", vm.Status);
    }

    [Fact]
    public async Task Ticking_it_again_on_a_Vanilla_client_waits_for_the_next_Modded_launch()
    {
        var (f, vm, item) = await OpenWithDeps(modded: false);
        item.Dependencies.Single(d => d.Id == "opt").Use = false;
        await vm.DependencyWork;

        item.Dependencies.Single(d => d.Id == "opt").Use = true;
        await vm.DependencyWork;

        Assert.False(f.Fs.File.Exists($"{Test}/stellar/deps/photo/opt.bin"));
        Assert.Equal("Not installed — installs at next Modded launch", item.Dependencies.Single(d => d.Id == "opt").StateText);
    }

    [Fact]
    public async Task A_plugin_without_dependencies_has_no_section()
    {
        var (_, vm) = await Open();
        var cm = vm.Rows.Single(r => r.Item.Entry.Id == "combatmeter").Item;

        vm.OpenPluginCommand.Execute(cm);
        await cm.DependencyRefresh;

        Assert.False(cm.HasDependencies);
    }

    // ---- Task 6 follow-up: Blocked and Failed reach the page ----

    private static async Task<(WorkspaceFixture f, ClientPluginsViewModel vm, StellarLauncher.App.ViewModels.PluginItemViewModel item)> OpenWithGameDeps(
        params StellarLauncher.Core.Model.PluginDependency[] deps)
    {
        var f = new WorkspaceFixture();
        f.Registry.Add(new StellarLauncher.Core.Model.PluginEntry("photo", "Photo", "d", "a", new[]
        {
            new StellarLauncher.Core.Model.PluginVersion("1.0.0", null, "Stellar.Photo.dll", "https://cdn/photo/1.0.0.dll",
                WorkspaceFixture.DllSha, "2.0.0", null, null, Dependencies: deps),
        }));
        f.AddClient("c2", "Test", Test, framework: "2.7.4");
        f.InstallPlugin(Test, "photo", "Stellar.Photo.dll", "1.0.0");
        f.Tabs = f.Tabs with { Plugins = w => new ClientPluginsViewModel(w) };
        f.Start();
        var ws = await f.OpenAsync("c2");
        ws.ShowPluginsCommand.Execute(null);
        var vm = (ClientPluginsViewModel)ws.TabContent!;
        await vm.ReloadAsync();
        return (f, vm, vm.Rows.Single().Item);
    }

    private static StellarLauncher.Core.Model.PluginDependency GameDep(string id, string to, string? sha = null) =>
        new(id, $"{id} name", "1.0", $"https://cdn/deps/{id}", sha ?? WorkspaceFixture.DllSha, WorkspaceFixture.DllBytes.Length, "file",
            new[] { new StellarLauncher.Core.Model.PluginDependencyFile(null, to) }, "game", ModdedOnly: true, Optional: true, License: "MIT", LicenseUrl: "https://l", SourceUrl: "https://s");

    // The main case: the player's own dxgi.dll sits where the dependency goes — the row says so before any launch.
    [Fact]
    public async Task A_players_own_file_at_the_destination_shows_Blocked_on_the_page()
    {
        var (f, vm, item) = await OpenWithGameDeps(GameDep("fx", "dxgi.dll"));
        f.Fs.AddFile($"{Test}/dxgi.dll", new MockFileData("player's own"));

        vm.OpenPluginCommand.Execute(item);
        await item.DependencyRefresh;

        var row = item.Dependencies.Single();
        Assert.Equal("Blocked — a file you installed is in the way: dxgi.dll", row.StateText);
        Assert.Equal("warn", row.StateClass);
        Assert.True(row.IsWarn);
        Assert.Equal("player's own", f.Fs.File.ReadAllText($"{Test}/dxgi.dll"));
    }

    // Failed is install-time only: the page shows the LAST EnsureAsync failure of this session while the
    // dependency is still not installed and its declaration is unchanged.
    [Fact]
    public async Task The_last_install_failure_shows_Failed_on_the_page()
    {
        var bad = GameDep("fx", "dxgi.dll", sha: new string('0', 64));   // served bytes never match
        var (f, vm, item) = await OpenWithGameDeps(bad);
        await f.Services.Core.Install.Dependencies.EnsureAsync(Test, "photo", new[] { bad }, new HashSet<string>(), CancellationToken.None);

        vm.OpenPluginCommand.Execute(item);
        await item.DependencyRefresh;

        var row = item.Dependencies.Single();
        Assert.StartsWith("Failed: ", row.StateText);
        Assert.Equal("bad", row.StateClass);
    }

    [Fact]
    public async Task Skipping_forgets_a_failure_and_re_ticking_shows_the_new_attempts_failure()
    {
        var bad = GameDep("fx", "dxgi.dll", sha: new string('0', 64));
        var (f, vm, item) = await OpenWithGameDeps(bad);
        await f.Services.Core.Install.Dependencies.EnsureAsync(Test, "photo", new[] { bad }, new HashSet<string>(), CancellationToken.None);
        vm.OpenPluginCommand.Execute(item);
        await item.DependencyRefresh;

        item.Dependencies.Single().Use = false;
        await vm.DependencyWork;
        Assert.Equal("Skipped", item.Dependencies.Single().StateText);
        item.Dependencies.Single().Use = true;
        await vm.DependencyWork;   // re-tick on Modded re-runs EnsureAsync: still fails → still Failed

        Assert.Equal("bad", item.Dependencies.Single().StateClass);
        Assert.Contains("Failed", vm.Status);
    }

    // Task 6 fix round, Important 2: a zip DIRECTORY mapping can't be checked before the download, so only
    // the install attempt finds the collision — the page must keep showing that Blocked afterwards.
    [Fact]
    public async Task An_install_time_Blocked_from_a_zip_directory_mapping_shows_on_the_page()
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        using (var w = new StreamWriter(zip.CreateEntry("shaders/a.fx").Open())) w.Write("shader");
        var bytes = ms.ToArray();
        var dep = new StellarLauncher.Core.Model.PluginDependency("pack", "Shader pack", "1.0", "https://cdn/deps/pack",
            Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, "zip",
            new[] { new StellarLauncher.Core.Model.PluginDependencyFile("shaders/", "shaders/") }, "game", Optional: true, License: "MIT", LicenseUrl: "https://l", SourceUrl: "https://s");
        var (f, vm, item) = await OpenWithGameDeps(dep);
        f.Downloads["https://cdn/deps/pack"] = bytes;
        f.Fs.AddFile($"{Test}/shaders/a.fx", new MockFileData("player's own shader"));
        var ensured = await f.Services.Core.Install.Dependencies.EnsureAsync(Test, "photo", new[] { dep }, new HashSet<string>(), CancellationToken.None);
        Assert.Equal(StellarLauncher.Core.Dependencies.DependencyState.Blocked, ensured.Single().State);

        vm.OpenPluginCommand.Execute(item);
        await item.DependencyRefresh;

        var row = item.Dependencies.Single();
        Assert.Equal("Blocked — a file you installed is in the way: shaders/a.fx", row.StateText);
        Assert.Equal("warn", row.StateClass);
    }

    // Fix round M3: while the client's game runs (session busy) the checkboxes are disabled and a toggle
    // changes nothing — no skip recorded, nothing removed or installed.
    [Fact]
    public async Task While_the_game_runs_dependency_checkboxes_are_disabled_and_toggles_change_nothing()
    {
        var (f, vm, item) = await OpenWithDeps();
        var session = f.Shell.Sessions.For(f.Shell.Config.Clients.Single(c => c.Id == "c2"));

        session.Begin(DateTimeOffset.UnixEpoch);   // Launching → IsBusy

        Assert.True(item.DependenciesLocked);
        Assert.All(item.Dependencies, d => Assert.False(d.CanChange));
        vm.SetDependencyUse(item, "opt", false);
        await vm.DependencyWork;
        Assert.Empty(f.Store.Load().Clients.Single(c => c.Id == "c2").SkippedDependencies);
        Assert.True(f.Fs.File.Exists($"{Test}/stellar/deps/photo/opt.bin"));
        Assert.Contains("close the game", vm.Status);

        session.Apply(new StellarLauncher.Core.Launch.ExitedEvent(0));   // game closed → editable again
        Assert.False(item.DependenciesLocked);
        Assert.True(item.Dependencies.Single(d => d.Id == "opt").CanChange);
        Assert.False(item.Dependencies.Single(d => d.Id == "req").CanChange);   // required stays fixed
    }

    // Fix round M4: a dependency with `requires` carries the mockup's "with <prerequisite>" chip.
    [Fact]
    public async Task A_dependency_with_requires_shows_a_with_prerequisite_chip()
    {
        var (_, _, item) = await OpenWithDeps();

        Assert.Equal("with opt name", item.Dependencies.Single(d => d.Id == "withopt").RequiresLabel);
        Assert.Null(item.Dependencies.Single(d => d.Id == "req").RequiresLabel);
        Assert.False(item.Dependencies.Single(d => d.Id == "req").HasRequires);
    }

    // Fix round 2, Minor 1: the page's background status must use a snapshot of SkippedDependencies taken on
    // the calling (UI) thread — never read the live profile list from the pool while the UI may mutate it.
    private sealed class StatusSpy(IDependencyService inner) : IDependencyService
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<string[]> Seen = new();
        public Task<IReadOnlyList<DependencyStatus>> EnsureAsync(string g, string p, IReadOnlyList<StellarLauncher.Core.Model.PluginDependency> d, ISet<string> s, CancellationToken ct) => inner.EnsureAsync(g, p, d, s, ct);
        public IReadOnlyList<DependencyStatus> Status(string g, string p, IReadOnlyList<StellarLauncher.Core.Model.PluginDependency> d, ISet<string> s)
        { Seen.Enqueue(s.OrderBy(x => x).ToArray()); return inner.Status(g, p, d, s); }
        public Task RemoveAsync(string g, string p, string d, CancellationToken ct = default) => inner.RemoveAsync(g, p, d, ct);
        public Task RemoveAllAsync(string g, string p, CancellationToken ct = default) => inner.RemoveAllAsync(g, p, ct);
        public Task ParkModdedOnlyAsync(string g, CancellationToken ct = default) => inner.ParkModdedOnlyAsync(g, ct);
        public Task UnparkModdedOnlyAsync(string g, CancellationToken ct = default) => inner.UnparkModdedOnlyAsync(g, ct);
        public IReadOnlyList<string> LedgerPluginIds(string g) => inner.LedgerPluginIds(g);
    }

    [Fact]
    public async Task The_background_status_uses_the_skip_list_as_it_was_when_the_refresh_started()
    {
        var f = new WorkspaceFixture();
        var dep = DepCase("opt", true);
        f.Registry.Add(new StellarLauncher.Core.Model.PluginEntry("photo", "Photo", "d", "a", new[]
        {
            new StellarLauncher.Core.Model.PluginVersion("1.0.0", null, "Stellar.Photo.dll", "https://cdn/photo/1.0.0.dll",
                WorkspaceFixture.DllSha, "2.0.0", null, null, Dependencies: new[] { dep }),
        }));
        f.AddClient("c2", "Test", Test, framework: "2.7.4");
        f.InstallPlugin(Test, "photo", "Stellar.Photo.dll", "1.0.0");
        var spy = new StatusSpy(new DependencyService(f.Fs, new HttpClient()));
        f.Dependencies = spy;
        f.Tabs = f.Tabs with { Plugins = w => new ClientPluginsViewModel(w) };
        f.Start();
        var ws = await f.OpenAsync("c2");
        ws.ShowPluginsCommand.Execute(null);
        var vm = (ClientPluginsViewModel)ws.TabContent!;
        await vm.ReloadAsync();
        var item = vm.Rows.Single().Item;
        var client = f.Shell.Config.Clients.Single(c => c.Id == "c2");

        // Hold every pool thread so the background half can't start before the "UI" mutates the list.
        ThreadPool.GetMinThreads(out var workers, out _);
        using var release = new ManualResetEventSlim(false);
        var blockers = Enumerable.Range(0, workers).Select(_ => Task.Factory.StartNew(() => release.Wait(5000),
            CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default)).ToArray();
        Task refresh;
        try
        {
            refresh = item.RefreshDependenciesAsync();
            client.SkippedDependencies.Add("photo/opt");   // the UI changes the profile right after
        }
        finally { release.Set(); }
        await Task.WhenAll(blockers);
        await refresh;

        Assert.True(spy.Seen.TryDequeue(out var seen));
        Assert.Empty(seen);   // the snapshot from when the refresh started — not the later mutation
    }
}

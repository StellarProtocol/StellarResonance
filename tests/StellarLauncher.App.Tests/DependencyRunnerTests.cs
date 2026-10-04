using StellarLauncher.App.Services;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using Xunit;

public class DependencyRunnerTests
{
    private sealed class FakeDependencyService : IDependencyService
    {
        public readonly List<(string GameMini, string PluginId, IReadOnlyList<PluginDependency> Deps, ISet<string> SkippedIds)> Calls = new();

        public Task<IReadOnlyList<DependencyStatus>> EnsureAsync(string gameMini, string pluginId,
            IReadOnlyList<PluginDependency> deps, ISet<string> skippedIds, CancellationToken ct)
        {
            Calls.Add((gameMini, pluginId, deps, skippedIds));
            // A skipped dependency reads Skipped, not Installed — matching the real DependencyService, and
            // needed for the "never reports Added for a skipped dependency" pin below.
            return Task.FromResult<IReadOnlyList<DependencyStatus>>(
                deps.Select(d => new DependencyStatus(d.Id, skippedIds.Contains(d.Id) ? DependencyState.Skipped : DependencyState.Installed, null)).ToList());
        }
        public readonly HashSet<string> NotInstalled = new();
        public IReadOnlyList<DependencyStatus> Status(string gameMini, string pluginId,
            IReadOnlyList<PluginDependency> deps, ISet<string> skippedIds) =>
            deps.Select(d => new DependencyStatus(d.Id, NotInstalled.Contains(d.Id) ? DependencyState.NotInstalled : DependencyState.Installed, null)).ToList();
        public Task RemoveAsync(string gameMini, string pluginId, string dependencyId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAllAsync(string gameMini, string pluginId, CancellationToken ct = default) => Task.CompletedTask;
        public Task ParkModdedOnlyAsync(string gameMini, CancellationToken ct = default) => Task.CompletedTask;
        public Task UnparkModdedOnlyAsync(string gameMini, IReadOnlySet<string>? keepParked = null, CancellationToken ct = default) => Task.CompletedTask;
        public readonly List<string> Ledgers = new();
        public IReadOnlyList<string> LedgerPluginIds(string gameMini) => Ledgers;
        public readonly Dictionary<string, string[]> ExistingEntryIds = new();
        public IReadOnlyList<LedgerEntry> LedgerEntries(string gameMini, string pluginId) =>
            ExistingEntryIds.TryGetValue(pluginId, out var ids)
                ? ids.Select(id => new LedgerEntry(id, "1.0", Array.Empty<LedgerFile>())).ToList()
                : Array.Empty<LedgerEntry>();
        public KeptDependencyDiskState KeptDiskState(string gameMini, string pluginId, string dependencyId) => KeptDependencyDiskState.Missing;
        public Task SetKeptAsync(string gameMini, string pluginId, bool kept, CancellationToken ct = default) => Task.CompletedTask;
        public bool IsKept(string gameMini, string pluginId) => false;
        public Task RequestReinstallAsync(string gameMini, string pluginId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> RemoveAllUnlessKeptAsync(string gameMini, string pluginId, CancellationToken ct = default) => Task.FromResult(true);
    }

    private static PluginDependency Dep(string id, bool optional = false) =>
        new(id, $"{id}-name", "1.0", $"https://cdn/{id}", new string('a', 64), 1, "file",
            new[] { new PluginDependencyFile(null, $"{id}.dll") }, "game", Optional: optional, License: "MIT", LicenseUrl: "https://l", SourceUrl: "https://s");

    private static PluginEntry EntryWithVersion(string id, string version, IReadOnlyList<PluginDependency>? deps = null, string? name = null) =>
        new(id, name ?? id, "d", null, new[] { new PluginVersion(version, null, $"{id}.dll", $"https://cdn/{id}.dll", "sha", "0.1.0", null, null, Dependencies: deps) });

    [Fact]
    public async Task Ensures_only_installed_plugins_whose_installed_version_declares_dependencies()
    {
        var withDeps = EntryWithVersion("p1", "1.0.0", new[] { Dep("a", optional: true) });
        var noDeps = EntryWithVersion("p2", "1.0.0");
        var versionNotInRegistry = EntryWithVersion("p3", "1.0.0", new[] { Dep("b") }); // installed "9.9.9" below

        var client = new ClientProfile { GameMiniDir = "/g", SkippedDependencies = new List<string> { "p1/a", "p2/x" } };
        var fake = new FakeDependencyService();
        var installed = new List<(PluginEntry Entry, string Version)>
        {
            (withDeps, "1.0.0"),
            (noDeps, "1.0.0"),
            (versionNotInRegistry, "9.9.9"), // not a version this entry declares
        };

        var lines = await DependencyRunner.EnsureForClientAsync(fake, client, installed, CancellationToken.None);

        var call = Assert.Single(fake.Calls);
        Assert.Equal("/g", call.GameMini);
        Assert.Equal("p1", call.PluginId);
        Assert.Equal(new[] { "a" }, call.SkippedIds.OrderBy(x => x));
        Assert.Equal(new[] { new DependencyLine("p1/a: Skipped", IsProblem: false) }, lines);   // "a" is skipped above
    }

    [Fact]
    public void Skipped_returns_only_this_plugins_ids_with_the_prefix_stripped()
    {
        var c = new ClientProfile { SkippedDependencies = new List<string> { "p1/a", "p1/b", "p2/a" } };
        var optional = new[] { Dep("a", optional: true), Dep("b", optional: true) };

        Assert.Equal(new[] { "a", "b" }, DependencyRunner.Skipped(c, "p1", optional).OrderBy(x => x));
        Assert.Equal(new[] { "a" }, DependencyRunner.Skipped(c, "p2", optional));
        Assert.Empty(DependencyRunner.Skipped(c, "p3", optional));
    }

    // Task 6 (e): only ids that are OPTIONAL in the plugin's installed version can be skipped — a required
    // id (or one the version no longer declares) is filtered out, so it is always installed.
    [Fact]
    public void Skipped_drops_required_and_undeclared_ids()
    {
        var c = new ClientProfile { SkippedDependencies = new List<string> { "p1/opt", "p1/req", "p1/gone" } };
        var deps = new[] { Dep("opt", optional: true), Dep("req") };

        Assert.Equal(new[] { "opt" }, DependencyRunner.Skipped(c, "p1", deps));
    }

    [Fact]
    public async Task EnsureForClient_never_passes_a_required_id_as_skipped()
    {
        var entry = EntryWithVersion("p1", "1.0.0", new[] { Dep("req"), Dep("opt", optional: true) });
        var client = new ClientProfile { GameMiniDir = "/g", SkippedDependencies = new List<string> { "p1/req", "p1/opt" } };
        var fake = new FakeDependencyService();

        await DependencyRunner.EnsureForClientAsync(fake, client, new[] { (entry, "1.0.0") }, CancellationToken.None);

        Assert.Equal(new[] { "opt" }, Assert.Single(fake.Calls).SkippedIds);
    }

    // Task 6 (d): before a plugin whose dependencies still need downloading, the runner reports
    // "Preparing <plugin>: <dependency names>…"; nothing is reported when everything is already installed.
    [Fact]
    public async Task EnsureForClient_reports_preparing_only_for_dependencies_not_yet_installed()
    {
        var needs = EntryWithVersion("p1", "1.0.0", new[] { Dep("a"), Dep("b") }, name: "Photo Thing");
        var done = EntryWithVersion("p2", "1.0.0", new[] { Dep("c") });
        var fake = new FakeDependencyService { NotInstalled = { "a" } };
        var progress = new List<string>();

        await DependencyRunner.EnsureForClientAsync(fake, new ClientProfile { GameMiniDir = "/g" },
            new[] { (needs, "1.0.0"), (done, "1.0.0") }, CancellationToken.None, progress.Add);

        Assert.Equal(new[] { "Preparing Photo Thing: a-name…" }, progress);
    }

    // Final review I3: an installed version that declares NO dependencies is still ensured (with an empty
    // declaration) when its plugin has a ledger, so whatever an earlier version placed is removed. Without a
    // ledger there is nothing to clean, and an unlisted version is never touched.
    [Fact]
    public async Task A_version_declaring_no_dependencies_is_ensured_empty_when_its_plugin_has_a_ledger()
    {
        var hadDeps = EntryWithVersion("p1", "2.0.0");
        var never = EntryWithVersion("p2", "1.0.0");
        var unlisted = EntryWithVersion("p3", "1.0.0");
        var fake = new FakeDependencyService { Ledgers = { "p1", "p3" } };

        await DependencyRunner.EnsureForClientAsync(fake, new ClientProfile { GameMiniDir = "/g" },
            new[] { (hadDeps, "2.0.0"), (never, "1.0.0"), (unlisted, "9.9.9") }, CancellationToken.None);

        var call = Assert.Single(fake.Calls);
        Assert.Equal("p1", call.PluginId);
        Assert.Empty(call.Deps);
    }

    // Owner decision ("install it ticked, tell me"): a pre-launch update/copy-set that brings a plugin
    // version declaring a NEW optional dependency (never in the ledger before — the player was never asked)
    // installs it ticked like a fresh install's default, and the status line says so, short and plain.
    [Fact]
    public async Task EnsureForClient_reports_a_newly_added_optional_dependency_on_the_status_line()
    {
        var entry = EntryWithVersion("p1", "2.0.0", new[] { Dep("reshade", optional: true) }, name: "Photo Studio");
        var fake = new FakeDependencyService { ExistingEntryIds = { ["p1"] = Array.Empty<string>() } };   // nothing in the ledger yet
        var progress = new List<string>();

        await DependencyRunner.EnsureForClientAsync(fake, new ClientProfile { GameMiniDir = "/g" },
            new[] { (entry, "2.0.0") }, CancellationToken.None, progress.Add);

        Assert.Contains("Added reshade-name for Photo Studio", progress);
        Assert.Empty(Assert.Single(fake.Calls).SkippedIds);   // not skipped, so it installs
    }

    // The other half of the pin: a dependency the player already opted out of stays skipped — no download,
    // no "Added" message — even though it is equally new to the ledger.
    [Fact]
    public async Task EnsureForClient_never_reports_added_for_a_dependency_the_player_already_skipped()
    {
        var entry = EntryWithVersion("p1", "2.0.0", new[] { Dep("reshade", optional: true) }, name: "Photo Studio");
        var client = new ClientProfile { GameMiniDir = "/g", SkippedDependencies = new List<string> { "p1/reshade" } };
        var fake = new FakeDependencyService { ExistingEntryIds = { ["p1"] = Array.Empty<string>() } };
        var progress = new List<string>();

        await DependencyRunner.EnsureForClientAsync(fake, client, new[] { (entry, "2.0.0") }, CancellationToken.None, progress.Add);

        Assert.DoesNotContain(progress, p => p.Contains("Added"));
        Assert.Equal(new[] { "reshade" }, Assert.Single(fake.Calls).SkippedIds);
    }

    // A dependency already in the ledger (not new) never gets an "Added" message even though it just
    // finished installing on this pass (e.g. it was pending from an earlier attempt).
    [Fact]
    public async Task EnsureForClient_never_reports_added_for_a_dependency_already_in_the_ledger()
    {
        var entry = EntryWithVersion("p1", "2.0.0", new[] { Dep("a", optional: true) }, name: "Photo Studio");
        var fake = new FakeDependencyService { ExistingEntryIds = { ["p1"] = new[] { "a" } } };   // already ledgered
        var progress = new List<string>();

        await DependencyRunner.EnsureForClientAsync(fake, new ClientProfile { GameMiniDir = "/g" },
            new[] { (entry, "2.0.0") }, CancellationToken.None, progress.Add);

        Assert.DoesNotContain(progress, p => p.Contains("Added"));
    }
}

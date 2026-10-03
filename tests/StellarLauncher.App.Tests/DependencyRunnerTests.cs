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
            return Task.FromResult<IReadOnlyList<DependencyStatus>>(
                deps.Select(d => new DependencyStatus(d.Id, DependencyState.Installed, null)).ToList());
        }
        public IReadOnlyList<DependencyStatus> Status(string gameMini, string pluginId,
            IReadOnlyList<PluginDependency> deps, ISet<string> skippedIds) => Array.Empty<DependencyStatus>();
        public void Remove(string gameMini, string pluginId, string dependencyId) { }
        public void RemoveAll(string gameMini, string pluginId) { }
        public void ParkModdedOnly(string gameMini) { }
        public void UnparkModdedOnly(string gameMini) { }
    }

    private static PluginDependency Dep(string id) =>
        new(id, id, "1.0", $"https://cdn/{id}", new string('a', 64), 1, "file",
            new[] { new PluginDependencyFile(null, $"{id}.dll") }, "game");

    private static PluginEntry EntryWithVersion(string id, string version, IReadOnlyList<PluginDependency>? deps = null) =>
        new(id, id, "d", null, new[] { new PluginVersion(version, null, $"{id}.dll", $"https://cdn/{id}.dll", "sha", "0.1.0", null, null, Dependencies: deps) });

    [Fact]
    public async Task Ensures_only_installed_plugins_whose_installed_version_declares_dependencies()
    {
        var withDeps = EntryWithVersion("p1", "1.0.0", new[] { Dep("a") });
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
        Assert.Equal(new[] { "p1/a: Installed" }, lines);
    }

    [Fact]
    public void Skipped_returns_only_this_plugins_ids_with_the_prefix_stripped()
    {
        var c = new ClientProfile { SkippedDependencies = new List<string> { "p1/a", "p1/b", "p2/a" } };

        Assert.Equal(new[] { "a", "b" }, DependencyRunner.Skipped(c, "p1").OrderBy(x => x));
        Assert.Equal(new[] { "a" }, DependencyRunner.Skipped(c, "p2"));
        Assert.Empty(DependencyRunner.Skipped(c, "p3"));
    }
}

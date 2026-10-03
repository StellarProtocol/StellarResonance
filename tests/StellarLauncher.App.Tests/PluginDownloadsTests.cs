using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using StellarLauncher.App.Services;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;
using Xunit;

/// <summary>Controller round: No EnsureAsync may ever run while a dependency is parked. A Modded
/// install-time call must unpark first (a dependency can still be parked from a prior vanilla launch);
/// a Vanilla client must skip ensuring entirely and defer to the next Modded launch.</summary>
public class PluginDownloadsTests
{
    private sealed class FakeDependencyService : IDependencyService
    {
        public readonly List<string> Calls = new();

        public Task<IReadOnlyList<DependencyStatus>> EnsureAsync(string gameMini, string pluginId,
            IReadOnlyList<PluginDependency> deps, ISet<string> skippedIds, CancellationToken ct)
        {
            Calls.Add($"Ensure:{pluginId}");
            return Task.FromResult<IReadOnlyList<DependencyStatus>>(
                deps.Select(d => new DependencyStatus(d.Id, DependencyState.Installed, null)).ToList());
        }
        public IReadOnlyList<DependencyStatus> Status(string gameMini, string pluginId,
            IReadOnlyList<PluginDependency> deps, ISet<string> skippedIds) => Array.Empty<DependencyStatus>();
        public void Remove(string gameMini, string pluginId, string dependencyId) { }
        public void RemoveAll(string gameMini, string pluginId) { }
        public void ParkModdedOnly(string gameMini) { }
        public void UnparkModdedOnly(string gameMini) => Calls.Add("Unpark");
    }

    private sealed class DllHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;
        public DllHandler(byte[] bytes) => _bytes = bytes;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_bytes) });
    }

    private static readonly byte[] DllBytes = Encoding.UTF8.GetBytes("dll-bytes");

    private static (PluginInstallDeps Deps, FakeDependencyService Fake) Build()
    {
        var fs = new MockFileSystem();
        var fake = new FakeDependencyService();
        var deps = new PluginInstallDeps(new Installer(fs), new PluginInstaller(fs), new HttpClient(new DllHandler(DllBytes)), fake);
        return (deps, fake);
    }

    private static PluginEntry EntryWithDependency()
    {
        var dep = new PluginDependency("dep1", "Dep One", "1.0", "https://cdn/dep1", new string('a', 64), 1, "file",
            new[] { new PluginDependencyFile(null, "dep1.dll") }, "game");
        var sha = Convert.ToHexString(SHA256.HashData(DllBytes)).ToLowerInvariant();
        var version = new PluginVersion("1.0.0", null, "P1.dll", "https://cdn/p1.dll", sha, "0.1.0", null, null, Dependencies: new[] { dep });
        return new PluginEntry("p1", "Plugin One", "d", null, new[] { version });
    }

    [Fact]
    public async Task Modded_install_unparks_before_ensuring_dependencies()
    {
        var (deps, fake) = Build();
        var entry = EntryWithDependency();

        await PluginDownloads.InstallAsync(deps, "/g", modded: true, entry, entry.Versions[0], null);

        Assert.Equal(new[] { "Unpark", "Ensure:p1" }, fake.Calls);
    }

    [Fact]
    public async Task Vanilla_install_never_ensures_and_defers_to_the_next_modded_launch()
    {
        var (deps, fake) = Build();
        var entry = EntryWithDependency();
        var messages = new List<string>();

        await PluginDownloads.InstallAsync(deps, "/g", modded: false, entry, entry.Versions[0], messages.Add);

        Assert.Empty(fake.Calls); // no UnparkModdedOnly, no EnsureAsync — files may still be parked
        Assert.Contains("will be installed at next modded launch", messages);
    }

    [Fact]
    public async Task Install_without_dependencies_never_touches_the_dependency_service()
    {
        var (deps, fake) = Build();
        var sha = Convert.ToHexString(SHA256.HashData(DllBytes)).ToLowerInvariant();
        var version = new PluginVersion("1.0.0", null, "P1.dll", "https://cdn/p1.dll", sha, "0.1.0", null, null);
        var entry = new PluginEntry("p1", "Plugin One", "d", null, new[] { version });

        await PluginDownloads.InstallAsync(deps, "/g", modded: true, entry, version, null);

        Assert.Empty(fake.Calls);
    }
}

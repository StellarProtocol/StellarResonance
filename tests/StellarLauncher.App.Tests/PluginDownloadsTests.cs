using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using StellarLauncher.App.Services;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;
using Xunit;

/// <summary>Controller round: No EnsureAsync may ever run while a dependency is parked. A Modded
/// install-time call must unpark first (a dependency can still be parked from a prior vanilla launch);
/// a Vanilla client must skip ensuring entirely and defer to the next Modded launch. Fix round 1,
/// Important 2: install/update must also honour the player's existing opt-outs
/// (<see cref="ClientProfile.SkippedDependencies"/>), not an always-empty set.</summary>
public class PluginDownloadsTests
{
    private sealed class FakeDependencyService : IDependencyService
    {
        public readonly List<string> Calls = new();
        public ISet<string>? LastSkippedIds;

        public Task<IReadOnlyList<DependencyStatus>> EnsureAsync(string gameMini, string pluginId,
            IReadOnlyList<PluginDependency> deps, ISet<string> skippedIds, CancellationToken ct)
        {
            Calls.Add($"Ensure:{pluginId}");
            LastSkippedIds = skippedIds;
            return Task.FromResult<IReadOnlyList<DependencyStatus>>(
                deps.Select(d => new DependencyStatus(d.Id, DependencyState.Installed, null)).ToList());
        }
        public IReadOnlyList<DependencyStatus> Status(string gameMini, string pluginId,
            IReadOnlyList<PluginDependency> deps, ISet<string> skippedIds) => Array.Empty<DependencyStatus>();
        public Task RemoveAsync(string gameMini, string pluginId, string dependencyId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAllAsync(string gameMini, string pluginId, CancellationToken ct = default) => Task.CompletedTask;
        public Task ParkModdedOnlyAsync(string gameMini, CancellationToken ct = default) => Task.CompletedTask;
        public bool ThrowOnUnpark;
        public Task UnparkModdedOnlyAsync(string gameMini, IReadOnlySet<string>? keepParked = null, CancellationToken ct = default)
        {
            Calls.Add("Unpark");
            if (ThrowOnUnpark) throw new IOException("parked file locked");
            return Task.CompletedTask;
        }
        public IReadOnlyList<string> LedgerPluginIds(string gameMini) => Array.Empty<string>();
        public IReadOnlyList<LedgerEntry> LedgerEntries(string gameMini, string pluginId) => Array.Empty<LedgerEntry>();

        public readonly List<string> Flags = new();
        public HashSet<string> Kept = new();
        public Task SetKeptAsync(string gameMini, string pluginId, bool kept, CancellationToken ct = default)
        { Flags.Add($"Kept:{pluginId}:{kept}"); return Task.CompletedTask; }
        public bool IsKept(string gameMini, string pluginId) => Kept.Contains(pluginId);
        public Task RequestReinstallAsync(string gameMini, string pluginId, CancellationToken ct = default)
        { Flags.Add($"Reinstall:{pluginId}"); return Task.CompletedTask; }
        public Task<bool> RemoveAllUnlessKeptAsync(string gameMini, string pluginId, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class DllHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;
        public DllHandler(byte[] bytes) => _bytes = bytes;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_bytes) });
    }

    private sealed class MultiUrlHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _byUrl;
        public MultiUrlHandler(Dictionary<string, byte[]> byUrl) => _byUrl = byUrl;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_byUrl[r.RequestUri!.ToString()]) });
    }

    private static readonly byte[] DllBytes = Encoding.UTF8.GetBytes("dll-bytes");

    private static (PluginInstallDeps Deps, FakeDependencyService Fake) Build()
    {
        var fs = new MockFileSystem();
        var fake = new FakeDependencyService();
        var deps = new PluginInstallDeps(new Installer(fs), new PluginInstaller(fs), new HttpClient(new DllHandler(DllBytes)), fake);
        return (deps, fake);
    }

    private static PluginEntry EntryWithDependency(bool optional = false)
    {
        var dep = new PluginDependency("dep1", "Dep One", "1.0", "https://cdn/dep1", new string('a', 64), 1, "file",
            new[] { new PluginDependencyFile(null, "dep1.dll") }, "game", Optional: optional, License: "MIT", LicenseUrl: "https://l", SourceUrl: "https://s");
        var sha = Convert.ToHexString(SHA256.HashData(DllBytes)).ToLowerInvariant();
        var version = new PluginVersion("1.0.0", null, "P1.dll", "https://cdn/p1.dll", sha, "0.1.0", null, null, Dependencies: new[] { dep });
        return new PluginEntry("p1", "Plugin One", "d", null, new[] { version });
    }

    private static ClientProfile Client(bool modded, params string[] skipped) =>
        new() { GameMiniDir = "/g", Modded = modded, SkippedDependencies = skipped.ToList() };

    [Fact]
    public async Task Modded_install_unparks_before_ensuring_dependencies()
    {
        var (deps, fake) = Build();
        var entry = EntryWithDependency();

        await PluginDownloads.InstallAsync(deps, Client(modded: true), entry, entry.Versions[0], null);

        Assert.Equal(new[] { "Unpark", "Ensure:p1" }, fake.Calls);
    }

    [Fact]
    public async Task Vanilla_install_never_ensures_and_defers_to_the_next_modded_launch()
    {
        var (deps, fake) = Build();
        var entry = EntryWithDependency();
        var messages = new List<string>();

        await PluginDownloads.InstallAsync(deps, Client(modded: false), entry, entry.Versions[0], messages.Add);

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

        await PluginDownloads.InstallAsync(deps, Client(modded: true), entry, version, null);

        Assert.Empty(fake.Calls);
    }

    // Fix round 1, Important 2: the player's existing opt-outs must be honoured on install/update too —
    // not just on the next launch's review. Before the fix, InstallAsync always passed an empty set.
    [Fact]
    public async Task Modded_install_passes_this_plugins_skip_set_to_EnsureAsync()
    {
        var (deps, fake) = Build();
        var entry = EntryWithDependency(optional: true);
        var client = Client(modded: true, "p1/dep1", "otherplugin/x"); // only "p1/…" ids are this plugin's

        await PluginDownloads.InstallAsync(deps, client, entry, entry.Versions[0], null);

        Assert.NotNull(fake.LastSkippedIds);
        Assert.Equal(new[] { "dep1" }, fake.LastSkippedIds);
    }

    // Fix round 1, Important 2: end-to-end against the REAL DependencyService — a skipped optional
    // dependency must never actually be installed on disk.
    [Fact]
    public async Task Skipped_optional_dependency_is_never_installed_on_disk()
    {
        var fs = new MockFileSystem();
        var depBytes = Encoding.UTF8.GetBytes("dep-bytes");
        var depSha = Convert.ToHexString(SHA256.HashData(depBytes)).ToLowerInvariant();
        var http = new HttpClient(new MultiUrlHandler(new Dictionary<string, byte[]>
        {
            ["https://cdn/p1.dll"] = DllBytes,
            ["https://cdn/dep1"] = depBytes,
        }));
        var real = new DependencyService(fs, http);
        var deps = new PluginInstallDeps(new Installer(fs), new PluginInstaller(fs), http, real);

        var dep = new PluginDependency("dep1", "Dep One", "1.0", "https://cdn/dep1", depSha, depBytes.Length, "file",
            new[] { new PluginDependencyFile(null, "dep1.dll") }, "game", Optional: true, License: "MIT", LicenseUrl: "https://l", SourceUrl: "https://s");
        var sha = Convert.ToHexString(SHA256.HashData(DllBytes)).ToLowerInvariant();
        var version = new PluginVersion("1.0.0", null, "P1.dll", "https://cdn/p1.dll", sha, "0.1.0", null, null, Dependencies: new[] { dep });
        var entry = new PluginEntry("p1", "Plugin One", "d", null, new[] { version });
        var client = Client(modded: true, "p1/dep1");

        await PluginDownloads.InstallAsync(deps, client, entry, version, null);

        Assert.True(fs.File.Exists("/g/stellar/plugins/p1/P1.dll"));
        Assert.False(fs.File.Exists("/g/dep1.dll"));
    }

    // Task 6 (a), from the Task 5 review: the plugin DLL is already installed when UnparkModdedOnly runs,
    // so an unpark error must not escape as a failed install — it is swallowed (like the review's
    // TryUnpark) and the dependencies are still ensured.
    [Fact]
    public async Task Unpark_failure_does_not_fail_the_install_and_still_ensures()
    {
        var (deps, fake) = Build();
        fake.ThrowOnUnpark = true;
        var entry = EntryWithDependency();
        var messages = new List<string>();

        await PluginDownloads.InstallAsync(deps, Client(modded: true), entry, entry.Versions[0], messages.Add);

        Assert.Equal(new[] { "Unpark", "Ensure:p1" }, fake.Calls);
        Assert.Contains("installed v1.0.0", messages);
    }

    // Task 6 (e): only OPTIONAL dependencies can be skipped — a stale/hand-edited profile entry naming a
    // required dependency must never reach EnsureAsync's skip set.
    [Fact]
    public async Task A_required_dependency_in_the_skip_list_is_still_installed()
    {
        var (deps, fake) = Build();
        var entry = EntryWithDependency(optional: false);

        await PluginDownloads.InstallAsync(deps, Client(modded: true, "p1/dep1"), entry, entry.Versions[0], null);

        Assert.NotNull(fake.LastSkippedIds);
        Assert.Empty(fake.LastSkippedIds!);
    }

    // v3 V3: installing a version that declares dependencies adopts any kept ones — on a Vanilla client too (a flag
    // write, never an ensure: the pinned Calls stay empty there).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Install_with_dependencies_adopts_kept_ones_on_any_client(bool modded)
    {
        var (deps, fake) = Build();
        var entry = EntryWithDependency();

        await PluginDownloads.InstallAsync(deps, Client(modded), entry, entry.Versions[0], null);

        Assert.Equal(new[] { "Kept:p1:False" }, fake.Flags);
        Assert.Equal(modded ? new[] { "Unpark", "Ensure:p1" } : Array.Empty<string>(), fake.Calls);
    }

    // v3 V2: "Also reinstall dependencies" on a Modded client: request, then the usual unpark → ensure consumes it.
    [Fact]
    public async Task Reinstall_with_dependencies_requests_it_before_the_ensure()
    {
        var (deps, fake) = Build();
        var entry = EntryWithDependency();

        await PluginDownloads.InstallAsync(deps, Client(modded: true), entry, entry.Versions[0], null, reinstallDependencies: true);

        Assert.Equal(new[] { "Kept:p1:False", "Reinstall:p1" }, fake.Flags);
        Assert.Equal(new[] { "Unpark", "Ensure:p1" }, fake.Calls);
    }

    // v3 V2: on a Vanilla client the dependency part waits for the next Modded launch, as an install does.
    [Fact]
    public async Task Vanilla_reinstall_records_the_request_and_defers_it()
    {
        var (deps, fake) = Build();
        var entry = EntryWithDependency();
        var messages = new List<string>();

        await PluginDownloads.InstallAsync(deps, Client(modded: false), entry, entry.Versions[0], messages.Add, reinstallDependencies: true);

        Assert.Empty(fake.Calls);   // no unpark, no ensure while files may be parked
        Assert.Equal(new[] { "Kept:p1:False", "Reinstall:p1" }, fake.Flags);
        Assert.Contains("dependencies will be reinstalled at next modded launch", messages);
    }
}

using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;
using Xunit;

public class PreLaunchReviewServiceTests
{
    private sealed class Registry : IPluginRegistryService
    {
        public Task<IReadOnlyList<PluginEntry>> FetchAllAsync(IEnumerable<Uri> urls, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PluginEntry>>(Array.Empty<PluginEntry>());
    }
    private sealed class Offline : IVersionService
    {
        public int Calls;
        public Task<FrameworkManifest> FetchAsync(Uri url, CancellationToken ct = default) { Calls++; throw new HttpRequestException("offline"); }
    }

    // Records Park/Unpark/Ensure calls so launch-order and fail-open can be pinned without touching disk.
    private sealed class FakeDependencyService : IDependencyService
    {
        public readonly List<string> Calls = new();
        public bool ThrowOnUnpark;
        public bool ThrowOnEnsure;

        public Task<IReadOnlyList<DependencyStatus>> EnsureAsync(string gameMini, string pluginId,
            IReadOnlyList<PluginDependency> deps, ISet<string> skippedIds, CancellationToken ct)
        {
            Calls.Add($"Ensure:{pluginId}");
            // Fix round 1: simulates an HttpClient request timeout — TaskCanceledException with no
            // relation to the caller's OWN ct (never cancelled here).
            if (ThrowOnEnsure) throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
            return Task.FromResult<IReadOnlyList<DependencyStatus>>(
                deps.Select(d => new DependencyStatus(d.Id, DependencyState.Installed, null)).ToList());
        }
        public IReadOnlyList<DependencyStatus> Status(string gameMini, string pluginId,
            IReadOnlyList<PluginDependency> deps, ISet<string> skippedIds) => Array.Empty<DependencyStatus>();
        public void Remove(string gameMini, string pluginId, string dependencyId) { }
        public void RemoveAll(string gameMini, string pluginId) { }
        public void ParkModdedOnly(string gameMini) => Calls.Add("Park");
        public void UnparkModdedOnly(string gameMini)
        {
            Calls.Add("Unpark");
            if (ThrowOnUnpark) throw new IOException("boom");
        }
    }

    // Shared by any test that needs one installed plugin whose installed version declares a dependency.
    private static PluginEntry OnePluginWithDependency()
    {
        var dependency = new PluginDependency("dep1", "Dep One", "1.0", "https://cdn/dep1", new string('a', 64), 1, "file",
            new[] { new PluginDependencyFile(null, "dep1.dll") }, "game");
        var version = new PluginVersion("1.0.0", null, "P1.dll", "https://cdn/p1.dll", "sha", "0.1.0", null, null,
            Dependencies: new[] { dependency });
        return new PluginEntry("p1", "Plugin One", "d", null, new[] { version });
    }

    private static (PluginInstallDeps Deps, ClientInventory Inventory) BuildInstalledP1(MockFileSystem fs, IDependencyService dependencies)
    {
        fs.AddFile("/g/stellar/plugins/p1/P1.dll", new MockFileData("x"));
        fs.AddFile("/g/stellar/plugins/p1/.plugin-version", new MockFileData("1.0.0"));
        var deps = new PluginInstallDeps(new Installer(fs), new PluginInstaller(fs), new HttpClient(), dependencies);
        var inventory = new ClientInventory(fs, deps.Installer, deps.Plugins, new DoorstopToggle(fs));
        return (deps, inventory);
    }

    private static PreLaunchReviewService Sut(IVersionService versions, Func<PreLaunchReviewViewModel, Task<PreLaunchResult>> prompt,
        IDependencyService? dependencies = null)
    {
        var fs = new MockFileSystem();
        var deps = new PluginInstallDeps(new Installer(fs), new PluginInstaller(fs), new HttpClient(), dependencies ?? new DependencyService(fs, new HttpClient()));
        var inventory = new ClientInventory(fs, deps.Installer, deps.Plugins, new DoorstopToggle(fs));
        return new PreLaunchReviewService(new RegistryCache(new Registry(), () => new LauncherConfig()), inventory, versions, deps, prompt);
    }

    // "Must not prompt" is asserted by COUNTING prompt calls. A throwing prompt delegate would be swallowed by the
    // service's whole-path fail-open and read as "true" — exactly the outcome these tests must be able to refute.
    private sealed class CountingPrompt
    {
        public int Calls;
        public Task<PreLaunchResult> Show(PreLaunchReviewViewModel _) { Calls++; return Task.FromResult(PreLaunchResult.Proceed); }
    }

    [Fact]
    public async Task Vanilla_client_skips_everything()
    {
        var versions = new Offline(); var prompt = new CountingPrompt();
        var sut = Sut(versions, prompt.Show);
        Assert.True(await sut.ReviewAsync(new ClientProfile { Modded = false, GameMiniDir = "/g" }, CancellationToken.None));
        Assert.Equal(0, versions.Calls);
        Assert.Equal(0, prompt.Calls);
    }

    [Fact]
    public async Task Offline_fails_open_without_prompting()
    {
        var prompt = new CountingPrompt();
        var sut = Sut(new Offline(), prompt.Show);
        Assert.True(await sut.ReviewAsync(new ClientProfile { Modded = true, GameMiniDir = "/g" }, CancellationToken.None));
        Assert.Equal(0, prompt.Calls);
    }

    [Fact]
    public async Task Empty_plan_fails_open_without_prompting()
    {
        var prompt = new CountingPrompt();
        var sut = Sut(new Online(), prompt.Show);
        Assert.True(await sut.ReviewAsync(new ClientProfile { Modded = true, GameMiniDir = "/g" }, CancellationToken.None));
        Assert.Equal(0, prompt.Calls);
    }

    [Fact]
    public async Task Vanilla_client_parks_modded_only_dependencies()
    {
        var fake = new FakeDependencyService();
        var prompt = new CountingPrompt();
        var sut = Sut(new Offline(), prompt.Show, fake);

        Assert.True(await sut.ReviewAsync(new ClientProfile { Modded = false, GameMiniDir = "/g" }, CancellationToken.None));

        Assert.Equal(new[] { "Park" }, fake.Calls);
    }

    [Fact]
    public async Task Modded_client_unparks_before_ensuring_dependencies()
    {
        var fs = new MockFileSystem();
        var entry = OnePluginWithDependency();
        var fake = new FakeDependencyService();
        var (deps, inventory) = BuildInstalledP1(fs, fake);
        var oneEntryRegistry = new RegistryCache(new OneEntryRegistry(entry), () => new LauncherConfig());
        var sut = new PreLaunchReviewService(oneEntryRegistry, inventory, new Online(), deps,
            _ => Task.FromResult(PreLaunchResult.Proceed));

        var result = await sut.ReviewAsync(new ClientProfile { Modded = true, GameMiniDir = "/g", AutoUpdateBeforeLaunch = true }, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(new[] { "Unpark", "Ensure:p1" }, fake.Calls);
    }

    [Fact]
    public async Task Throwing_dependency_service_still_fails_open()
    {
        var fake = new FakeDependencyService { ThrowOnUnpark = true };
        var prompt = new CountingPrompt();
        var sut = Sut(new Offline(), prompt.Show, fake);

        var result = await sut.ReviewAsync(new ClientProfile { Modded = true, GameMiniDir = "/g" }, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(0, prompt.Calls);
    }

    // Fix round 1, Minor 1: UnparkModdedOnly failing must not skip the rest of the review — the registry
    // check and EnsureDependenciesAsync still run afterward (before the fix, the bare call sat inside the
    // outer try, so its failure fell straight into the whole-path catch and "Ensure:p1" never ran).
    [Fact]
    public async Task Unpark_failure_does_not_skip_the_rest_of_the_review()
    {
        var fs = new MockFileSystem();
        var entry = OnePluginWithDependency();
        var fake = new FakeDependencyService { ThrowOnUnpark = true };
        var (deps, inventory) = BuildInstalledP1(fs, fake);
        var oneEntryRegistry = new RegistryCache(new OneEntryRegistry(entry), () => new LauncherConfig());
        var sut = new PreLaunchReviewService(oneEntryRegistry, inventory, new Online(), deps,
            _ => Task.FromResult(PreLaunchResult.Proceed));

        var result = await sut.ReviewAsync(new ClientProfile { Modded = true, GameMiniDir = "/g", AutoUpdateBeforeLaunch = true }, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(new[] { "Unpark", "Ensure:p1" }, fake.Calls); // unpark attempted (and failed) but ensure still ran
    }

    // Fix round 1, Important 1: an HttpClient request timeout surfaces as TaskCanceledException with the
    // CALLER's own ct never cancelled — EnsureDependenciesAsync must swallow it (fail-open), never let it
    // unwind as a "cancelled review" that ClientSessions would silently read as "don't launch".
    [Fact]
    public async Task HttpClient_timeout_during_ensure_still_fails_open()
    {
        var fs = new MockFileSystem();
        var entry = OnePluginWithDependency();
        var fake = new FakeDependencyService { ThrowOnEnsure = true };
        var (deps, inventory) = BuildInstalledP1(fs, fake);
        var oneEntryRegistry = new RegistryCache(new OneEntryRegistry(entry), () => new LauncherConfig());
        var sut = new PreLaunchReviewService(oneEntryRegistry, inventory, new Online(), deps,
            _ => Task.FromResult(PreLaunchResult.Proceed));

        var result = await sut.ReviewAsync(new ClientProfile { Modded = true, GameMiniDir = "/g", AutoUpdateBeforeLaunch = true }, CancellationToken.None);

        Assert.True(result);
    }

    private sealed class OneEntryRegistry : IPluginRegistryService
    {
        private readonly PluginEntry _entry;
        public OneEntryRegistry(PluginEntry entry) => _entry = entry;
        public Task<IReadOnlyList<PluginEntry>> FetchAllAsync(IEnumerable<Uri> urls, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PluginEntry>>(new[] { _entry });
    }

    [Fact]
    public async Task Prompt_returns_cancel_results_in_false()
    {
        var fs = new MockFileSystem();
        // Seed the installed framework version marker
        var versionPath = "/g/BepInEx/plugins/Stellar.Framework/.stellar-version";
        fs.AddFile(versionPath, new MockFileData("1.0.0"));

        var deps = new PluginInstallDeps(new Installer(fs), new PluginInstaller(fs), new HttpClient(), new DependencyService(fs, new HttpClient()));
        var inventory = new ClientInventory(fs, deps.Installer, deps.Plugins, new DoorstopToggle(fs));
        var versions = new UpdateAvailableVersion();
        var promptCalls = 0;
        var sut = new PreLaunchReviewService(
            new RegistryCache(new Registry(), () => new LauncherConfig()),
            inventory,
            versions,
            deps,
            _ => { promptCalls++; return Task.FromResult(PreLaunchResult.Cancel); });

        var result = await sut.ReviewAsync(new ClientProfile { Modded = true, GameMiniDir = "/g", AutoUpdateBeforeLaunch = true }, CancellationToken.None);

        Assert.False(result);
        Assert.Equal(1, promptCalls);
    }

    [Fact]
    public async Task Prompt_returns_proceed_results_in_true()
    {
        var fs = new MockFileSystem();
        // Seed the installed framework version marker
        var versionPath = "/g/BepInEx/plugins/Stellar.Framework/.stellar-version";
        fs.AddFile(versionPath, new MockFileData("1.0.0"));

        var deps = new PluginInstallDeps(new Installer(fs), new PluginInstaller(fs), new HttpClient(), new DependencyService(fs, new HttpClient()));
        var inventory = new ClientInventory(fs, deps.Installer, deps.Plugins, new DoorstopToggle(fs));
        var versions = new UpdateAvailableVersion();
        var promptCalls = 0;
        var sut = new PreLaunchReviewService(
            new RegistryCache(new Registry(), () => new LauncherConfig()),
            inventory,
            versions,
            deps,
            _ => { promptCalls++; return Task.FromResult(PreLaunchResult.Proceed); });

        var result = await sut.ReviewAsync(new ClientProfile { Modded = true, GameMiniDir = "/g", AutoUpdateBeforeLaunch = true }, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, promptCalls);
    }

    private sealed class Online : IVersionService
    {
        public Task<FrameworkManifest> FetchAsync(Uri url, CancellationToken ct = default)
        {
            var changelog = new Changelog(new[] { "added feature" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            var version = new VersionManifest("1.0.0", "2026-09-11", "https://example.com/v1.0.0", "abc123", "0.1.0", changelog);
            var manifest = new FrameworkManifest("1.0.0", null, new[] { version });
            return Task.FromResult(manifest);
        }
    }

    private sealed class UpdateAvailableVersion : IVersionService
    {
        public Task<FrameworkManifest> FetchAsync(Uri url, CancellationToken ct = default)
        {
            var changelog = new Changelog(new[] { "new feature" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            // MinLauncherVersion set to 0.0.0 to ensure launcher is supported
            var version = new VersionManifest("2.0.0", "2026-09-11", "https://example.com/v2.0.0", "def456", "0.0.0", changelog);
            var manifest = new FrameworkManifest("2.0.0", null, new[] { version });
            return Task.FromResult(manifest);
        }
    }
}

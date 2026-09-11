using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels;
using StellarLauncher.Core.Clients;
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

    private static PreLaunchReviewService Sut(IVersionService versions, Func<PreLaunchReviewViewModel, Task<PreLaunchResult>> prompt)
    {
        var fs = new MockFileSystem();
        var deps = new PluginInstallDeps(new Installer(fs), new PluginInstaller(fs), new HttpClient());
        var inventory = new ClientInventory(fs, deps.Installer, deps.Plugins, new DoorstopToggle(fs));
        return new PreLaunchReviewService(new RegistryCache(new Registry(), () => new LauncherConfig()), inventory, versions, deps, prompt);
    }

    [Fact]
    public async Task Vanilla_client_skips_everything()
    {
        var versions = new Offline();
        var sut = Sut(versions, _ => throw new Exception("must not prompt"));
        Assert.True(await sut.ReviewAsync(new ClientProfile { Modded = false, GameMiniDir = "/g" }, CancellationToken.None));
        Assert.Equal(0, versions.Calls);
    }

    [Fact]
    public async Task Offline_fails_open_without_prompting()
    {
        var sut = Sut(new Offline(), _ => throw new Exception("must not prompt"));
        Assert.True(await sut.ReviewAsync(new ClientProfile { Modded = true, GameMiniDir = "/g" }, CancellationToken.None));
    }

    [Fact]
    public async Task Empty_plan_fails_open_without_prompting()
    {
        var versions = new Online();
        var sut = Sut(versions, _ => throw new Exception("must not prompt"));
        Assert.True(await sut.ReviewAsync(new ClientProfile { Modded = true, GameMiniDir = "/g" }, CancellationToken.None));
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
}

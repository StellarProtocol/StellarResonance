using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;
using Xunit;

/// <summary>Final review I6 (controller decision): a DISABLED plugin's <c>moddedOnly</c> files are parked on a
/// Modded launch — exactly as a Vanilla launch parks everyone's — and restored once it is enabled again. Real
/// <see cref="DependencyService"/> and <see cref="PluginInstaller"/> over a MockFileSystem; nothing downloads.</summary>
public class DisabledPluginParkingTests
{
    private const string Fx = "/g/dxgi.dll";
    private const string Parked = "/g/stellar/deps-parked/p2/dxgi.dll";
    private static readonly byte[] FxBytes = { 7, 7, 7 };

    private sealed class OneEntry(PluginEntry e) : IPluginRegistryService
    {
        public Task<IReadOnlyList<PluginEntry>> FetchAllAsync(IEnumerable<Uri> urls, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PluginEntry>>(new[] { e });
    }

    private sealed class Manifest(string latest) : IVersionService
    {
        public Task<FrameworkManifest> FetchAsync(Uri url, CancellationToken ct = default)
        {
            var log = new Changelog(new[] { "x" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            return Task.FromResult(new FrameworkManifest(latest, null,
                new[] { new VersionManifest(latest, "2026-10-04", $"https://example.com/{latest}", "abc", "0.0.0", log) }));
        }
    }

    /// <summary>p1 installed (no dependencies); p2 has a placed moddedOnly file and sits in plugins-disabled.</summary>
    private static (MockFileSystem Fs, PluginInstallDeps Deps, ClientInventory Inv) Setup()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/g/stellar/plugins/p1/P1.dll", new MockFileData("x"));
        fs.AddFile("/g/stellar/plugins/p1/.plugin-version", new MockFileData("1.0.0"));
        fs.AddFile("/g/stellar/plugins-disabled/p2/P2.dll", new MockFileData("x"));
        fs.AddFile("/g/stellar/plugins-disabled/p2/.plugin-version", new MockFileData("1.0.0"));
        fs.AddFile(Fx, new MockFileData(FxBytes));
        new DependencyLedgerStore(fs).Write("/g", new DependencyLedger("p2", new[] { new LedgerEntry("fx", "1",
            new[] { new LedgerFile("dxgi.dll", Convert.ToHexString(SHA256.HashData(FxBytes)), true) }) }));
        var deps = new PluginInstallDeps(new Installer(fs), new PluginInstaller(fs), new HttpClient(), new DependencyService(fs, new HttpClient()));
        return (fs, deps, new ClientInventory(fs, deps.Installer, deps.Plugins, new DoorstopToggle(fs)));
    }

    private static PluginEntry P1() => new("p1", "Plugin One", "d", null,
        new[] { new PluginVersion("1.0.0", null, "P1.dll", "https://cdn/p1.dll", "sha", "0.1.0", null, null) });

    private static PreLaunchReviewService Review(PluginInstallDeps deps, ClientInventory inv, IVersionService versions,
        Func<PreLaunchReviewViewModel, Task<PreLaunchResult>> prompt) =>
        new(new RegistryCache(new OneEntry(P1()), () => new LauncherConfig()), inv, versions, deps, prompt);

    private static ClientProfile Modded() => new() { Modded = true, GameMiniDir = "/g", AutoUpdateBeforeLaunch = true };

    [Fact]
    public async Task A_disabled_plugins_modded_only_file_is_parked_on_a_modded_launch_and_restored_once_enabled()
    {
        var (fs, deps, inv) = Setup();
        var review = Review(deps, inv, new Manifest("1.0.0"), _ => Task.FromResult(PreLaunchResult.Proceed));

        Assert.True(await review.ReviewAsync(Modded(), CancellationToken.None));
        Assert.False(fs.File.Exists(Fx));
        Assert.Equal(FxBytes, fs.File.ReadAllBytes(Parked));

        deps.Plugins.Enable("/g", "p2");
        Assert.True(await review.ReviewAsync(Modded(), CancellationToken.None));
        Assert.Equal(FxBytes, fs.File.ReadAllBytes(Fx));
        Assert.False(fs.File.Exists(Parked));
    }

    [Fact]
    public async Task A_plugin_disabled_in_the_pre_launch_dialog_has_its_modded_only_file_parked_before_the_game_starts()
    {
        var (fs, deps, inv) = Setup();
        deps.Plugins.Enable("/g", "p2");
        fs.AddFile("/g/BepInEx/plugins/Stellar.Framework/.stellar-version", new MockFileData("1.0.0"));
        var prompts = 0;
        var review = Review(deps, inv, new Manifest("2.0.0"), _ =>
        {
            prompts++;
            deps.Plugins.Disable("/g", "p2");   // what the dialog's "disable" choice does
            return Task.FromResult(PreLaunchResult.Proceed);
        });

        Assert.True(await review.ReviewAsync(Modded(), CancellationToken.None));

        Assert.Equal(1, prompts);
        Assert.False(fs.File.Exists(Fx));
        Assert.Equal(FxBytes, fs.File.ReadAllBytes(Parked));
    }

    [Fact]
    public async Task An_enabled_plugins_modded_only_file_stays_in_place_on_a_modded_launch()
    {
        var (fs, deps, inv) = Setup();
        deps.Plugins.Enable("/g", "p2");
        var review = Review(deps, inv, new Manifest("1.0.0"), _ => Task.FromResult(PreLaunchResult.Proceed));

        Assert.True(await review.ReviewAsync(Modded(), CancellationToken.None));
        Assert.Equal(FxBytes, fs.File.ReadAllBytes(Fx));
    }
}

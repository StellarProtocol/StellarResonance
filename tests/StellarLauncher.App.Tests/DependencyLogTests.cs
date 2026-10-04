using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using Xunit;

/// <summary>Final review M-f: a dependency park / unpark / ensure failure writes ONE line to the always-on log
/// file (a Release build without debug logging has no other sink); a successful outcome writes nothing there.</summary>
public class DependencyLogTests
{
    private sealed class Failing : IDependencyService
    {
        public bool FailPark, FailUnpark;
        public DependencyState EnsureState = DependencyState.Installed;
        public Task<IReadOnlyList<DependencyStatus>> EnsureAsync(string g, string p, IReadOnlyList<PluginDependency> d, ISet<string> s, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DependencyStatus>>(d.Select(x => new DependencyStatus(x.Id, EnsureState, EnsureState == DependencyState.Installed ? null : "checksum mismatch")).ToList());
        public IReadOnlyList<DependencyStatus> Status(string g, string p, IReadOnlyList<PluginDependency> d, ISet<string> s) => Array.Empty<DependencyStatus>();
        public Task RemoveAsync(string g, string p, string id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAllAsync(string g, string p, CancellationToken ct = default) => Task.CompletedTask;
        public Task ParkModdedOnlyAsync(string g, CancellationToken ct = default) => FailPark ? throw new IOException("park boom") : Task.CompletedTask;
        public Task UnparkModdedOnlyAsync(string g, IReadOnlySet<string>? keepParked = null, CancellationToken ct = default) =>
            FailUnpark ? throw new IOException("unpark boom") : Task.CompletedTask;
        public IReadOnlyList<string> LedgerPluginIds(string g) => Array.Empty<string>();
        public Task SetKeptAsync(string gameMini, string pluginId, bool kept, CancellationToken ct = default) => Task.CompletedTask;
        public bool IsKept(string gameMini, string pluginId) => false;
        public Task RequestReinstallAsync(string gameMini, string pluginId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static PluginInstallDeps Deps(IDependencyService svc)
    {
        var fs = new System.IO.Abstractions.TestingHelpers.MockFileSystem();
        return new PluginInstallDeps(new StellarLauncher.Core.Services.Installer(fs), new StellarLauncher.Core.Services.PluginInstaller(fs), new HttpClient(), svc);
    }

    private static PluginDependency Dep() => new("fx", "Fx", "1.0", "https://cdn/fx", new string('a', 64), 1, "file",
        new[] { new PluginDependencyFile(null, "fx.dll") }, "game", License: "MIT", LicenseUrl: "https://l", SourceUrl: "https://s");

    /// <summary>Runs <paramref name="act"/> with the always-on file pointed at a temp file; returns that file's
    /// lines mentioning <paramref name="marker"/> (other test classes may log concurrently).</summary>
    private static async Task<string[]> Capture(string marker, Func<Task> act)
    {
        var path = Path.Combine(Path.GetTempPath(), $"deps-log-{Guid.NewGuid():N}.log");
        DependencyLog.AlwaysOnFile = path;
        try { await act(); }
        finally { DependencyLog.AlwaysOnFile = null; }
        try { return File.Exists(path) ? File.ReadAllLines(path).Where(l => l.Contains(marker)).ToArray() : Array.Empty<string>(); }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task A_failed_restore_writes_one_always_on_line()
    {
        var lines = await Capture("/g-unpark", () => DependencyRunner.RestoreForModdedAsync(Deps(new Failing { FailUnpark = true }), "/g-unpark", CancellationToken.None));
        Assert.Contains("unpark boom", Assert.Single(lines));
    }

    [Fact]
    public async Task A_failed_vanilla_park_writes_one_always_on_line_and_still_launches()
    {
        var review = new PreLaunchReviewService(null!, null!, null!, Deps(new Failing { FailPark = true }), _ => Task.FromResult(PreLaunchResult.Proceed));
        var ok = false;
        var lines = await Capture("ParkLogClient", async () =>
            ok = await review.ReviewAsync(new ClientProfile { Name = "ParkLogClient", Modded = false, GameMiniDir = "/g" }, CancellationToken.None));
        Assert.True(ok);
        Assert.Contains("park boom", Assert.Single(lines));
    }

    [Fact]
    public async Task A_failed_dependency_writes_one_line_and_an_installed_one_writes_none()
    {
        var failed = await Capture("/g-ensure-bad", () =>
            DependencyRunner.EnsurePluginAsync(Deps(new Failing { EnsureState = DependencyState.Failed }), "/g-ensure-bad", "p", new[] { Dep() }, new HashSet<string>()));
        var ok = await Capture("/g-ensure-ok", () =>
            DependencyRunner.EnsurePluginAsync(Deps(new Failing()), "/g-ensure-ok", "p", new[] { Dep() }, new HashSet<string>()));

        Assert.Contains("p/fx: Failed — checksum mismatch", Assert.Single(failed));
        Assert.Empty(ok);
    }
}

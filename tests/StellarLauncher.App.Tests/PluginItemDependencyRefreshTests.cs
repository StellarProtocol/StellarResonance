using StellarLauncher.App.ViewModels;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using Xunit;

/// <summary>Fix round M1: the page's dependency status (ledger reads + hashing) is computed off the
/// caller's (UI) thread; only the newest refresh applies its rows.</summary>
public class PluginItemDependencyRefreshTests
{
    private sealed class SlowActions : IPluginActions
    {
        public readonly ManualResetEventSlim Gate = new(false);
        public int? StatusThread;
        public Task InstallAsync(PluginItemViewModel item) => Task.CompletedTask;
        public Task RemoveAsync(PluginItemViewModel item) => Task.CompletedTask;
        public Task EnableAsync(PluginItemViewModel item) => Task.CompletedTask;
        public IReadOnlyList<DependencyStatus> DependencyStatus(PluginItemViewModel item)
        {
            StatusThread = Environment.CurrentManagedThreadId;
            Gate.Wait(TimeSpan.FromSeconds(5));
            return item.ShownDependencies.Select(d => new DependencyStatus(d.Id, DependencyState.Installed, null)).ToList();
        }
        // Nothing UI-bound to snapshot in this fake; it only hops threads like the real one.
        public Task<IReadOnlyList<DependencyStatus>> DependencyStatusAsync(PluginItemViewModel item) => Task.Run(() => DependencyStatus(item));
        public void SetDependencyUse(PluginItemViewModel item, string dependencyId, bool use) { }
    }

    private static PluginItemViewModel Item(IPluginActions actions)
    {
        var dep = new PluginDependency("fx", "Fx", "1.0", "https://cdn/fx", new string('a', 64), 1, "file",
            new[] { new PluginDependencyFile(null, "fx.dll") }, "game", License: "MIT", LicenseUrl: "https://l", SourceUrl: "https://s");
        var entry = new PluginEntry("p", "P", "d", null, new[]
        {
            new PluginVersion("1.0.0", null, "P.dll", "https://cdn/p.dll", "sha", "0.1.0", null, null, Dependencies: new[] { dep }),
        });
        return new PluginItemViewModel(entry, true, "1.0.0", "2.8.0", actions);
    }

    [Fact]
    public async Task Status_is_computed_off_the_calling_thread_and_the_call_returns_at_once()
    {
        var actions = new SlowActions();
        var item = Item(actions);
        var caller = Environment.CurrentManagedThreadId;

        var started = System.Diagnostics.Stopwatch.StartNew();
        var refresh = item.RefreshDependenciesAsync();
        var returnedAfter = started.ElapsedMilliseconds;
        actions.Gate.Set();
        await refresh;

        Assert.True(returnedAfter < 1000, $"RefreshDependenciesAsync blocked the caller for {returnedAfter} ms");
        Assert.NotEqual(caller, actions.StatusThread);
        Assert.Equal("Installed", Assert.Single(item.Dependencies).StateText);
    }
}

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
        public readonly ManualResetEventSlim Entered = new(false);
        public int? StatusThread;
        public Task InstallAsync(PluginItemViewModel item) => Task.CompletedTask;
        public Task RemoveAsync(PluginItemViewModel item) => Task.CompletedTask;
        public Task EnableAsync(PluginItemViewModel item) => Task.CompletedTask;
        private IReadOnlyList<DependencyStatus> DependencyStatus(PluginItemViewModel item)
        {
            StatusThread = Environment.CurrentManagedThreadId;
            Entered.Set();
            Gate.Wait(TimeSpan.FromSeconds(5));
            return item.ShownDependencies.Select(d => new DependencyStatus(d.Id, DependencyState.Installed, null)).ToList();
        }
        // Nothing UI-bound to snapshot in this fake; it only hops threads like the real one.
        public Task<IReadOnlyList<DependencyStatus>> DependencyStatusAsync(PluginItemViewModel item) => Task.Run(() => DependencyStatus(item));
        public void SetDependencyUse(PluginItemViewModel item, string dependencyId, bool use) { }
        public bool HasInstallStep(PluginItemViewModel item) => false;
        public Task<bool> DependenciesKeptAsync(PluginItemViewModel item) => Task.FromResult(false);
        public Task RemoveKeptDependenciesAsync(PluginItemViewModel item) => Task.CompletedTask;
        public Task<IReadOnlyList<LedgerEntry>> KeptLedgerEntriesAsync(PluginItemViewModel item) => Task.FromResult<IReadOnlyList<LedgerEntry>>(Array.Empty<LedgerEntry>());
        public Task<IReadOnlyDictionary<string, KeptDependencyDiskState>> KeptDiskStatesAsync(PluginItemViewModel item) =>
            Task.FromResult<IReadOnlyDictionary<string, KeptDependencyDiskState>>(new Dictionary<string, KeptDependencyDiskState>());
    }

    // Final-review M-2: in kept mode, RefreshCoreAsync never awaits the shown version's status task (its
    // result is irrelevant then) — but it must still OBSERVE it, so a fault in it never surfaces later as an
    // unobserved task exception.
    private sealed class KeptFaultingStatusActions : IPluginActions
    {
        public Task InstallAsync(PluginItemViewModel item) => Task.CompletedTask;
        public Task RemoveAsync(PluginItemViewModel item) => Task.CompletedTask;
        public Task EnableAsync(PluginItemViewModel item) => Task.CompletedTask;
        public Task<IReadOnlyList<DependencyStatus>> DependencyStatusAsync(PluginItemViewModel item) =>
            Task.FromException<IReadOnlyList<DependencyStatus>>(new InvalidOperationException("irrelevant in kept mode"));
        public void SetDependencyUse(PluginItemViewModel item, string dependencyId, bool use) { }
        public bool HasInstallStep(PluginItemViewModel item) => false;
        public Task<bool> DependenciesKeptAsync(PluginItemViewModel item) => Task.FromResult(true);
        public Task RemoveKeptDependenciesAsync(PluginItemViewModel item) => Task.CompletedTask;
        public Task<IReadOnlyList<LedgerEntry>> KeptLedgerEntriesAsync(PluginItemViewModel item) => Task.FromResult<IReadOnlyList<LedgerEntry>>(Array.Empty<LedgerEntry>());
        public Task<IReadOnlyDictionary<string, KeptDependencyDiskState>> KeptDiskStatesAsync(PluginItemViewModel item) =>
            Task.FromResult<IReadOnlyDictionary<string, KeptDependencyDiskState>>(new Dictionary<string, KeptDependencyDiskState>());
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task RunKeptRefreshWithFaultingStatusAsync() => await Item(new KeptFaultingStatusActions()).RefreshDependenciesAsync();

    [Fact]
    public async Task Kept_mode_observes_the_unused_status_task_so_a_fault_in_it_never_becomes_unobserved()
    {
        UnobservedTaskExceptionEventArgs? caught = null;
        void Handler(object? s, UnobservedTaskExceptionEventArgs e) { caught = e; e.SetObserved(); }
        TaskScheduler.UnobservedTaskException += Handler;
        try
        {
            await RunKeptRefreshWithFaultingStatusAsync();
            GC.Collect(); GC.WaitForPendingFinalizers();
            GC.Collect(); GC.WaitForPendingFinalizers();
            Assert.Null(caught);
        }
        finally { TaskScheduler.UnobservedTaskException -= Handler; }
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
        // Hold the calling thread until the status work has started. Releasing the gate first and then awaiting
        // let the caller's own pool thread go back to the pool and pick up the queued status work itself, so the
        // thread ids could match by chance (CI run 37201407333). While the caller blocks here, the status work can
        // only be running on another thread.
        Assert.True(actions.Entered.Wait(TimeSpan.FromSeconds(5)), "the status work never started");
        actions.Gate.Set();
        await refresh;

        Assert.True(returnedAfter < 1000, $"RefreshDependenciesAsync blocked the caller for {returnedAfter} ms");
        Assert.NotEqual(caller, actions.StatusThread);
        Assert.Equal("Installed", Assert.Single(item.Dependencies).StateText);
    }
}

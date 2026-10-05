using System.Collections.Generic;
using System.Threading.Tasks;
using StellarLauncher.Core.Dependencies;

namespace StellarLauncher.App.ViewModels;

/// <summary>What a plugin row asks its host to do (install the selected version, remove, re-enable), and the
/// state of its declared dependencies on this client.</summary>
public interface IPluginActions
{
    Task InstallAsync(PluginItemViewModel item);
    Task RemoveAsync(PluginItemViewModel item);
    Task EnableAsync(PluginItemViewModel item);

    /// <summary>Read-only status of <see cref="PluginItemViewModel.ShownDependencies"/> on this client,
    /// computed off the calling (UI) thread. Fix round 2,
    /// Minor 1: every implementation must snapshot whatever UI-thread state it reads (the profile's skip
    /// list) BEFORE leaving the thread — deliberately no default body.</summary>
    Task<IReadOnlyList<DependencyStatus>> DependencyStatusAsync(PluginItemViewModel item);

    /// <summary>Records whether an OPTIONAL dependency is used (required ones are ignored).</summary>
    void SetDependencyUse(PluginItemViewModel item, string dependencyId, bool use);

    /// <summary>v3 V1/V2: true when installing the selected version opens a step dialog (the page then shows no inline Confirm).</summary>
    bool HasInstallStep(PluginItemViewModel item);

    /// <summary>v3 V3: whether this plugin's dependencies were kept when it was removed (read off the UI thread).</summary>
    Task<bool> DependenciesKeptAsync(PluginItemViewModel item);

    /// <summary>v3 V3: the page's "Remove kept dependencies".</summary>
    Task RemoveKeptDependenciesAsync(PluginItemViewModel item);

    /// <summary>v3 V3 review fix round 3 (4): this plugin's ledger entries exactly as kept (read off the UI thread) —
    /// the kept section's rows and Vanilla clause must reflect what's REALLY on disk, not what the shown version
    /// happens to declare.</summary>
    Task<IReadOnlyList<LedgerEntry>> KeptLedgerEntriesAsync(PluginItemViewModel item);

    /// <summary>Final-review I-2: each kept dependency's disk state right now (read off the UI thread), keyed by
    /// dependency id — "Kept"/"Kept · moved aside"/"Missing" instead of a blanket "Installed" pill.</summary>
    Task<IReadOnlyDictionary<string, KeptDependencyDiskState>> KeptDiskStatesAsync(PluginItemViewModel item);

    /// <summary>2.1.2 (discoverability): this plugin's currently-skipped OPTIONAL dependency ids — a pure
    /// profile lookup, no I/O, safe to call synchronously on the UI thread. Backs the plugin list row's
    /// extras pill, which must be available before the detail page's own async status refresh ever runs
    /// (the player may never have opened the detail page this session).</summary>
    ISet<string> SkippedOptionalIds(PluginItemViewModel item);
}

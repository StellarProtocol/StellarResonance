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

    /// <summary>Read-only status of <see cref="PluginItemViewModel.ShownDependencies"/> on this client.</summary>
    IReadOnlyList<DependencyStatus> DependencyStatus(PluginItemViewModel item);

    /// <summary>Same as <see cref="DependencyStatus"/>, computed off the calling (UI) thread. Fix round 2,
    /// Minor 1: every implementation must snapshot whatever UI-thread state it reads (the profile's skip
    /// list) BEFORE leaving the thread — deliberately no default body.</summary>
    Task<IReadOnlyList<DependencyStatus>> DependencyStatusAsync(PluginItemViewModel item);

    /// <summary>Records whether an OPTIONAL dependency is used (required ones are ignored).</summary>
    void SetDependencyUse(PluginItemViewModel item, string dependencyId, bool use);
}

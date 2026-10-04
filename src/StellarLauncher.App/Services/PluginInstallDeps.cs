using System.Net.Http;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.Services;

/// <summary>The collaborators every plugin install path needs, bundled to keep constructors ≤ 6 deps
/// (a record is exempt from that guardrail itself — this IS the bundle).</summary>
public sealed record PluginInstallDeps(IInstaller Installer, IPluginInstaller Plugins, HttpClient Http, IDependencyService Dependencies)
{
    /// <summary>The in-memory record of the last install-time dependency problems: <see cref="Dependencies"/>
    /// itself when it is the <see cref="RecordingDependencyService"/> (as the app wires it — final review M-g:
    /// one instance, not passed twice); null shows none.</summary>
    public IDependencyOutcomes? Outcomes => Dependencies as IDependencyOutcomes;
}

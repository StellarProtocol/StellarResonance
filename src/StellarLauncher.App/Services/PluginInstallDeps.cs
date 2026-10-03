using System.Net.Http;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.Services;

/// <summary>The collaborators every plugin install path needs, bundled to keep constructors ≤ 6 deps
/// (a record is exempt from that guardrail itself — this IS the bundle). <see cref="Outcomes"/> is the
/// in-memory record of the last install-time dependency failures (normally the same
/// <see cref="RecordingDependencyService"/> instance as <see cref="Dependencies"/>); null shows none.</summary>
public sealed record PluginInstallDeps(IInstaller Installer, IPluginInstaller Plugins, HttpClient Http, IDependencyService Dependencies,
    IDependencyOutcomes? Outcomes = null);

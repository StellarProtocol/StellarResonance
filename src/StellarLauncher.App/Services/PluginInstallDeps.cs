using System.Net.Http;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.Services;

/// <summary>The four collaborators every plugin install path needs, bundled to keep constructors ≤ 6 deps
/// (a record is exempt from that guardrail itself — this IS the bundle).</summary>
public sealed record PluginInstallDeps(IInstaller Installer, IPluginInstaller Plugins, HttpClient Http, IDependencyService Dependencies);

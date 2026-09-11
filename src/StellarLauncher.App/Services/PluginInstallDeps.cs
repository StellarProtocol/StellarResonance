using System.Net.Http;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.Services;

/// <summary>The three collaborators every plugin install path needs, bundled to keep constructors ≤ 6 deps.</summary>
public sealed record PluginInstallDeps(IInstaller Installer, IPluginInstaller Plugins, HttpClient Http);

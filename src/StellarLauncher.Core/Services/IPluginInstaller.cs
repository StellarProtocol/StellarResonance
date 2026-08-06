using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StellarLauncher.Core.Services;

public interface IPluginInstaller
{
    Task InstallAsync(Stream dll, string expectedSha256, string gameMiniDir,
                      string pluginId, string dllFileName, string version, CancellationToken ct = default);
    void Remove(string gameMiniDir, string pluginId, string? dllFileName = null);
    bool IsInstalled(string gameMiniDir, string pluginId);
    string? InstalledVersion(string gameMiniDir, string pluginId);

    /// <summary>Path of the plugin DLL anywhere under stellar/plugins, or null — detects installs that
    /// weren't created by this launcher (no marker), e.g. from a previous launcher.</summary>
    string? FindInstalledDll(string gameMiniDir, string dllFileName);

    /// <summary>Park an installed plugin outside the framework scan path (stellar/plugins-disabled) so
    /// the game boots without it. Reversible via <see cref="Enable"/>.</summary>
    void Disable(string gameMiniDir, string pluginId);
    /// <summary>Restore a disabled plugin back into stellar/plugins.</summary>
    void Enable(string gameMiniDir, string pluginId);
    bool IsDisabled(string gameMiniDir, string pluginId);
}

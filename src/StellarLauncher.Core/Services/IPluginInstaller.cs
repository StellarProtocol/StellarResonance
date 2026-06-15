using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StellarLauncher.Core.Services;

public interface IPluginInstaller
{
    Task InstallAsync(Stream dll, string expectedSha256, string gameMiniDir,
                      string pluginId, string dllFileName, string version, CancellationToken ct = default);
    void Remove(string gameMiniDir, string pluginId);
    bool IsInstalled(string gameMiniDir, string pluginId);
    string? InstalledVersion(string gameMiniDir, string pluginId);
}

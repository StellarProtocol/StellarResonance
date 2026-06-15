using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StellarLauncher.Core.Services;

public interface ILauncherSelfUpdater
{
    Task StageAsync(Stream zip, string expectedSha256, string stagingDir, CancellationToken ct = default);
    string BuildWindowsSwapScript(string stagingDir, string installDir, string exeName);
    void ApplyAndRestart(string stagingDir, string installDir, string exeName, bool isWindows);
}

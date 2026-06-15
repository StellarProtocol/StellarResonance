// src/StellarLauncher.Core/Services/IInstaller.cs
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StellarLauncher.Core.Services;

public interface IInstaller
{
    Task InstallAsync(Stream bundleZip, string expectedSha256, string gameMiniDir,
                      string version, CancellationToken ct = default);
    string? ReadInstalledVersion(string gameMiniDir);
}

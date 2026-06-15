using System;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Services;

public interface ILauncherUpdateService
{
    Task<LauncherManifest> FetchAsync(Uri manifestUrl, CancellationToken ct = default);
}

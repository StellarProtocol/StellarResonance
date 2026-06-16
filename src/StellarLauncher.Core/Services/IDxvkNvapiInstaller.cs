using System.Threading;
using System.Threading.Tasks;

namespace StellarLauncher.Core.Services;

public interface IDxvkNvapiInstaller
{
    /// <summary>
    /// Ensure the latest DXVK-NVAPI is installed into <paramref name="winePrefix"/> (downloads + extracts
    /// nvapi64.dll/nvapi.dll into the prefix, skipping if already current). Returns a human status line.
    /// Best-effort: throws only on hard failures the caller surfaces.
    /// </summary>
    Task<string> EnsureAsync(string winePrefix, CancellationToken ct = default);
}

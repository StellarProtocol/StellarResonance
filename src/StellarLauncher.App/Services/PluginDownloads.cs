using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.Services;

public static class PluginDownloads
{
    /// <summary>Download one plugin version and install it under its canonical DLL name into one client.</summary>
    public static async Task InstallAsync(PluginInstallDeps deps, string gameMini, PluginEntry entry, PluginVersion v,
        Action<string>? status)
    {
        using var buffer = new MemoryStream();
        long lastTick = -1;
        var progress = new Progress<DownloadProgress>(p =>
        {
            long tick = p.Fraction is { } f ? (long)(f * 100) : p.BytesRead >> 20;
            if (tick != lastTick) { lastTick = tick; status?.Invoke(DownloadStatus.Line("downloading…", p)); }
        });
        await deps.Http.DownloadToAsync(new Uri(v.DllUrl), buffer, progress);
        buffer.Position = 0;
        var fileName = v.Dll ?? Path.GetFileName(new Uri(v.DllUrl).LocalPath);
        await deps.Plugins.InstallAsync(buffer, v.Sha256, gameMini, entry.Id, fileName, v.Version, CancellationToken.None);
        status?.Invoke($"installed v{v.Version}");
    }
}

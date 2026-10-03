using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.Services;

public static class PluginDownloads
{
    /// <summary>Download one plugin version and install it under its canonical DLL name into one client.
    /// When the version declares dependencies: a Modded client unparks (a dependency may still be parked
    /// from a prior vanilla launch) before ensuring them immediately, honouring whatever this plugin's
    /// dependencies the player already opted out of (<see cref="ClientProfile.SkippedDependencies"/> —
    /// fix round 1, Important 2); a Vanilla client defers entirely — EnsureAsync must never run while
    /// files are parked, so the next Modded launch installs them.</summary>
    public static async Task InstallAsync(PluginInstallDeps deps, ClientProfile client, PluginEntry entry, PluginVersion v,
        Action<string>? status)
    {
        var gameMini = client.GameMiniDir;
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

        if (v.Dependencies is not { Count: > 0 } pluginDeps) return;
        if (!client.Modded) { status?.Invoke("will be installed at next modded launch"); return; }

        // No EnsureAsync may run while files are parked — unpark first, every time (idempotent).
        deps.Dependencies.UnparkModdedOnly(gameMini);
        // Fix round 1, Important 2: honour whatever the player already opted out of for THIS plugin —
        // an install/update must not silently re-install a dependency they unticked.
        var skipped = DependencyRunner.Skipped(client, entry.Id);
        foreach (var s in await deps.Dependencies.EnsureAsync(gameMini, entry.Id, pluginDeps, skipped, CancellationToken.None))
            if (s.State != DependencyState.Installed)
                status?.Invoke($"{entry.Id}/{s.DependencyId}: {s.State}{(s.Detail is null ? "" : " — " + s.Detail)}");
    }
}

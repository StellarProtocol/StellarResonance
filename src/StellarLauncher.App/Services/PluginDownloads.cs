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
    /// v3: adopts kept dependencies; <paramref name="reinstallDependencies"/> requests a fresh download and
    /// verify (Modded: now; Vanilla: at the next Modded launch). When the version declares dependencies: a
    /// Modded client unparks (a dependency may still be parked from a prior vanilla launch) before ensuring
    /// them immediately, honouring whatever this plugin's dependencies the player already opted out of
    /// (<see cref="ClientProfile.SkippedDependencies"/> — fix round 1, Important 2); a Vanilla client defers
    /// entirely — EnsureAsync must never run while files are parked, so the next Modded launch installs them.</summary>
    public static async Task InstallAsync(PluginInstallDeps deps, ClientProfile client, PluginEntry entry, PluginVersion v,
        Action<string>? status, bool reinstallDependencies = false)
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
        // v3 V3: the plugin is installed again, so dependencies kept when it was removed are its own again — on a Vanilla
        // client too (a flag write only: no file moves, safe while files are parked). v3 V2: "Also reinstall dependencies"
        // asks the next ensure to download + verify them afresh.
        await TryFlagAsync(gameMini, entry.Id, "adopting kept dependencies",
            () => deps.Dependencies.SetKeptAsync(gameMini, entry.Id, false));
        if (reinstallDependencies)
            await TryFlagAsync(gameMini, entry.Id, "requesting a dependency reinstall",
                () => deps.Dependencies.RequestReinstallAsync(gameMini, entry.Id));
        if (!client.Modded)
        {
            status?.Invoke(reinstallDependencies ? "dependencies will be reinstalled at next modded launch" : "will be installed at next modded launch");
            return;
        }

        // No EnsureAsync may run while files are parked — restore first, every time (idempotent, fail-open:
        // the plugin IS installed at this point, so a restore error must not surface as a failed install).
        // Fix round 1, Important 2: honour whatever the player already opted out of for THIS plugin — an
        // install/update must not silently re-install a dependency they unticked (optional ones only).
        // Final review M-g: the same restore-then-ensure as the launch review and the page's re-tick.
        var skipped = DependencyRunner.Skipped(client, entry.Id, pluginDeps);
        foreach (var s in await DependencyRunner.EnsurePluginAsync(deps, gameMini, entry.Id, pluginDeps, skipped))
            if (s.State != DependencyState.Installed)
                status?.Invoke(DependencyRunner.Line(entry.Id, s));
    }

    /// <summary>A ledger flag write after the plugin itself is installed: fail-open (the install succeeded), one
    /// always-on log line on failure (final review M-f).</summary>
    private static async Task TryFlagAsync(string gameMini, string pluginId, string what, Func<Task> write)
    {
        try { await write(); }
        catch (Exception ex) { DependencyLog.Failure(gameMini, $"{pluginId}: {what} failed — {ex.Message}"); }
    }
}

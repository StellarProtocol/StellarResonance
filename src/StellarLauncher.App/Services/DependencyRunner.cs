using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;

namespace StellarLauncher.App.Services;

/// <summary>Drives <see cref="IDependencyService"/> for every installed plugin on one client, and resolves
/// which of a plugin's dependencies the player opted out of (<see cref="ClientProfile.SkippedDependencies"/>).</summary>
public static class DependencyRunner
{
    public static ISet<string> Skipped(ClientProfile c, string pluginId) =>
        c.SkippedDependencies.Where(s => s.StartsWith(pluginId + "/", StringComparison.Ordinal))
            .Select(s => s[(pluginId.Length + 1)..]).ToHashSet();

    public static async Task<IReadOnlyList<string>> EnsureForClientAsync(IDependencyService svc, ClientProfile c,
        IReadOnlyList<(PluginEntry Entry, string Version)> installed, CancellationToken ct)
    {
        var lines = new List<string>();
        foreach (var (entry, version) in installed)
        {
            var deps = entry.Versions.FirstOrDefault(v => v.Version == version)?.Dependencies;
            if (deps is not { Count: > 0 }) continue;
            foreach (var s in await svc.EnsureAsync(c.GameMiniDir, entry.Id, deps, Skipped(c, entry.Id), ct))
                lines.Add($"{entry.Id}/{s.DependencyId}: {s.State}{(s.Detail is null ? "" : " — " + s.Detail)}");
        }
        return lines;
    }
}

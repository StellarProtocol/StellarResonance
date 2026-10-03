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
    /// <summary>This plugin's opted-out dependency ids — only those that are <c>optional</c> in
    /// <paramref name="deps"/> (the version actually installed/shown). A required dependency can never be
    /// skipped, whatever the profile says (e.g. it was optional in an older version, or the file was edited).</summary>
    public static ISet<string> Skipped(ClientProfile c, string pluginId, IReadOnlyList<PluginDependency> deps)
    {
        var optional = deps.Where(d => d.Optional).Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        return c.SkippedDependencies.Where(s => s.StartsWith(pluginId + "/", StringComparison.Ordinal))
            .Select(s => s[(pluginId.Length + 1)..]).Where(optional.Contains).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Ensures every installed plugin's dependencies. Before a plugin whose dependencies still need
    /// downloading, reports "Preparing &lt;plugin&gt;: &lt;dependency names&gt;…" through
    /// <paramref name="progress"/>. Returns one status line per dependency.</summary>
    public static async Task<IReadOnlyList<string>> EnsureForClientAsync(IDependencyService svc, ClientProfile c,
        IReadOnlyList<(PluginEntry Entry, string Version)> installed, CancellationToken ct, Action<string>? progress = null)
    {
        var lines = new List<string>();
        var ledgers = LedgerIds(svc, c.GameMiniDir);
        foreach (var (entry, version) in installed)
        {
            // A version the registry doesn't list: its declaration is unknown, so nothing is touched.
            if (entry.Versions.FirstOrDefault(v => v.Version == version) is not { } v) continue;
            var deps = v.Dependencies ?? Array.Empty<PluginDependency>();
            // Final review I3: a version that declares nothing still gets ensured when this plugin has a ledger —
            // an empty declaration is what removes everything an earlier version placed.
            if (deps.Count == 0 && !ledgers.Contains(entry.Id)) continue;
            var skipped = Skipped(c, entry.Id, deps);
            if (progress is not null && PendingNames(svc, c.GameMiniDir, entry.Id, deps, skipped) is { Length: > 0 } names)
                progress($"Preparing {entry.Name}: {names}…");
            foreach (var s in await svc.EnsureAsync(c.GameMiniDir, entry.Id, deps, skipped, ct))
                lines.Add(Line(entry.Id, s));
        }
        return lines;
    }

    private static ISet<string> LedgerIds(IDependencyService svc, string gameMini)
    {
        try { return svc.LedgerPluginIds(gameMini).ToHashSet(StringComparer.Ordinal); }
        catch (Exception) { return new HashSet<string>(); }
    }

    public static string Line(string pluginId, DependencyStatus s) =>
        $"{pluginId}/{s.DependencyId}: {s.State}{(s.Detail is null ? "" : " — " + s.Detail)}";

    /// <summary>Names of the dependencies a read-only <see cref="IDependencyService.Status"/> says are not
    /// installed yet ("" when none, or when the check itself fails — it is only feedback).</summary>
    private static string PendingNames(IDependencyService svc, string gameMini, string pluginId,
        IReadOnlyList<PluginDependency> deps, ISet<string> skipped)
    {
        try
        {
            var pending = svc.Status(gameMini, pluginId, deps, skipped)
                .Where(s => s.State == DependencyState.NotInstalled).Select(s => s.DependencyId).ToHashSet();
            return string.Join(", ", deps.Where(d => pending.Contains(d.Id)).Select(d => d.Name));
        }
        catch (Exception) { return ""; }
    }
}

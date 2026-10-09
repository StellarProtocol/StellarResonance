using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.Services;

/// <summary>One dependency status line ("&lt;plugin&gt;/&lt;dependency&gt;: &lt;state&gt;[ — detail]");
/// <see cref="IsProblem"/> for a Failed or Blocked outcome (the always-on log's lines — M-f).</summary>
public sealed record DependencyLine(string Text, bool IsProblem);

/// <summary>Drives <see cref="IDependencyService"/> for every installed plugin on one client, and resolves
/// which of a plugin's dependencies the player opted out of (<see cref="ClientProfile.SkippedDependencies"/>).
/// Final review M-g: also the ONE place the Modded "restore parked files, then ensure" sequence lives — the
/// launch review, an install/update and the plugin page's re-tick all go through
/// <see cref="RestoreForModdedAsync"/> / <see cref="EnsurePluginAsync"/>.</summary>
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

    /// <summary>Before anything is ensured on a Modded client: every parked <c>moddedOnly</c> file comes back,
    /// except those of a DISABLED plugin, which are parked instead (final review I6) and come back once it is
    /// enabled. Fail-open: a failure writes one always-on log line and never stops the caller. Returns the
    /// disabled plugins it parked for.</summary>
    /// <param name="unlessStillDisabled">For a second pass after something that can only DISABLE plugins (the
    /// pre-launch dialog): nothing is done when exactly these plugins are still the disabled ones.</param>
    public static async Task<IReadOnlySet<string>> RestoreForModdedAsync(PluginInstallDeps deps, string gameMini,
        CancellationToken ct, IReadOnlySet<string>? unlessStillDisabled = null)
    {
        var disabled = DisabledWithLedger(deps, gameMini);
        if (unlessStillDisabled is not null && disabled.SetEquals(unlessStillDisabled)) return disabled;
        try { await deps.Dependencies.UnparkModdedOnlyAsync(gameMini, disabled, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { DependencyLog.Failure(gameMini, $"restoring parked dependencies failed — {ex.Message}"); }
        return disabled;
    }

    /// <summary>Restore (<see cref="RestoreForModdedAsync"/>), then ensure ONE plugin's dependencies with the
    /// skip set the caller snapshotted — an install/update and the plugin page's re-tick. Problems are logged
    /// (always-on) and returned; only a real failure of the ensure itself throws.</summary>
    public static async Task<IReadOnlyList<DependencyStatus>> EnsurePluginAsync(PluginInstallDeps deps, string gameMini,
        string pluginId, IReadOnlyList<PluginDependency> pluginDeps, ISet<string> skipped)
    {
        await RestoreForModdedAsync(deps, gameMini, CancellationToken.None);
        var results = await deps.Dependencies.EnsureAsync(gameMini, pluginId, pluginDeps, skipped, CancellationToken.None);
        foreach (var s in results.Where(IsProblem)) DependencyLog.Failure(gameMini, Line(pluginId, s));
        return results;
    }

    /// <summary>Plugin ids that have a dependency ledger here but are disabled (only under plugins-disabled).
    /// Never throws — on any error nothing is treated as disabled.</summary>
    private static IReadOnlySet<string> DisabledWithLedger(PluginInstallDeps deps, string gameMini)
    {
        try
        {
            return deps.Dependencies.LedgerPluginIds(gameMini)
                .Where(id => deps.Plugins.IsDisabled(gameMini, id) && !deps.Plugins.IsInstalled(gameMini, id))
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception) { return new HashSet<string>(); }
    }

    /// <summary>Ensures every installed plugin's dependencies. Before a plugin whose dependencies still need
    /// downloading, reports "Preparing &lt;plugin&gt;: &lt;dependency names&gt;…" through
    /// <paramref name="progress"/>. Owner decision ("install it ticked, tell me"): a pre-launch version bump
    /// that declares an optional dependency not yet in this plugin's ledger — one the player was never asked
    /// about, because no step dialog runs here — installs it the same as a fresh install's ticked default,
    /// and this reports "Added &lt;dependency&gt; for &lt;plugin&gt;" once it actually lands (a player who
    /// already opted out via <see cref="ClientProfile.SkippedDependencies"/> never sees either the download
    /// or the message). R-3: this is the ONLY caller of this method — the copy-set path
    /// (<c>ClientPluginsViewModel.CopySetFrom</c>) never goes through it, so a new optional dependency on an
    /// already-installed plugin copied from another client is NOT announced; it only installs plugins the
    /// target client doesn't already have, each going through the ordinary fresh-install ticked-by-default
    /// flow with no announcement needed. Returns one status line per dependency.</summary>
    public static async Task<IReadOnlyList<DependencyLine>> EnsureForClientAsync(IDependencyService svc, ClientProfile c,
        IReadOnlyList<(PluginEntry Entry, string Version)> installed, CancellationToken ct, Action<string>? progress = null)
    {
        var lines = new List<DependencyLine>();
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
            ISet<string>? before = null;
            if (progress is not null)
            {
                if (PendingNames(svc, c.GameMiniDir, entry.Id, deps, skipped) is { Length: > 0 } names)
                    progress(ReviewLines.Preparing(entry.Name, names, entry));
                before = ExistingDependencyIds(svc, c.GameMiniDir, entry.Id);   // snapshot BEFORE the ensure
            }
            var results = await svc.EnsureAsync(c.GameMiniDir, entry.Id, deps, skipped, ct);
            foreach (var s in results) lines.Add(new DependencyLine(Line(entry.Id, s), IsProblem(s)));
            if (before is not null)
                foreach (var s in results)
                    if (s.State == DependencyState.Installed && !before.Contains(s.DependencyId)
                        && deps.FirstOrDefault(d => d.Id == s.DependencyId) is { Optional: true } d)
                        progress!(ReviewLines.Added(d.Name, entry.Name, entry));
        }
        return lines;
    }

    /// <summary>The dependency ids this plugin's ledger already records, right now — used to tell a
    /// genuinely NEW optional dependency (never ledgered before this ensure) from one merely finishing an
    /// earlier attempt. Best-effort: a read failure is treated as "nothing recorded" (never crashes the
    /// ensure, and at worst over-reports once).</summary>
    private static ISet<string> ExistingDependencyIds(IDependencyService svc, string gameMini, string pluginId)
    {
        try { return svc.LedgerEntries(gameMini, pluginId).Select(e => e.DependencyId).ToHashSet(StringComparer.Ordinal); }
        catch (Exception) { return new HashSet<string>(); }
    }

    private static bool IsProblem(DependencyStatus s) => s.State is DependencyState.Failed or DependencyState.Blocked;

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

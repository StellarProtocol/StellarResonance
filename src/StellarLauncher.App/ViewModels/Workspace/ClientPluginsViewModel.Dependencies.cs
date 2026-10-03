using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.App.Services;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Launch;
using StellarLauncher.Core.Model;

namespace StellarLauncher.App.ViewModels.Workspace;

/// <summary>The plugin page's DEPENDENCIES section (Task 6): status of the shown version's dependencies on
/// this client, and the player's choice to use or skip an OPTIONAL one (stored as "&lt;plugin&gt;/&lt;dep&gt;"
/// in <see cref="Core.Clients.ClientProfile.SkippedDependencies"/>).</summary>
public sealed partial class ClientPluginsViewModel
{
    private readonly Action<LaunchSession> _onSession;

    /// <summary>The most recent dependency change started by <see cref="SetDependencyUse"/> — removal,
    /// background install, and the row refresh after it (completed when idle). Awaited by tests.</summary>
    public Task DependencyWork { get; private set; } = Task.CompletedTask;

    public void Dispose() => _ws.Session.Changed -= _onSession;

    /// <summary>The read-only status (Installed / NotInstalled / Skipped / Blocked) — except that a dependency
    /// still NotInstalled whose last install attempt this session was Failed or Blocked (same declaration
    /// since) shows that outcome instead: Status is offline and can't see a download failure, nor a
    /// collision inside a zip directory mapping. Called off the UI thread (fix round M1).</summary>
    public IReadOnlyList<DependencyStatus> DependencyStatus(PluginItemViewModel item) => StatusFor(Snapshot(item));

    /// <summary>Fix round 2, Minor 1: everything read from UI-thread state — the shown dependencies, the game
    /// folder and a COPY of this plugin's skip set (from the profile's live SkippedDependencies list) — is
    /// captured here, on the calling thread, before the ledger reads and hashing move to the pool.</summary>
    public Task<IReadOnlyList<DependencyStatus>> DependencyStatusAsync(PluginItemViewModel item)
    {
        var snapshot = Snapshot(item);
        return Task.Run(() => StatusFor(snapshot));
    }

    private sealed record StatusSnapshot(string GameMini, string PluginId, IReadOnlyList<PluginDependency> Deps, ISet<string> Skipped);

    private StatusSnapshot Snapshot(PluginItemViewModel item)
    {
        var deps = item.ShownDependencies;
        // Skipped() enumerates the live list into a NEW set right here — that set is the snapshot.
        return new StatusSnapshot(_ws.Client.GameMiniDir, item.Entry.Id, deps, DependencyRunner.Skipped(_ws.Client, item.Entry.Id, deps));
    }

    private IReadOnlyList<DependencyStatus> StatusFor(StatusSnapshot snap)
    {
        var (gameMini, pluginId, deps, skipped) = snap;
        var install = _ws.Services.Core.Install;
        var statuses = install.Dependencies.Status(gameMini, pluginId, deps, skipped);
        if (install.Outcomes is not { } outcomes) return statuses;
        return statuses.Select(s =>
            s.State == DependencyState.NotInstalled && deps.FirstOrDefault(d => d.Id == s.DependencyId) is { } d
            && outcomes.LastProblem(gameMini, pluginId, d) is { } problem ? problem : s).ToList();
    }

    public void SetDependencyUse(PluginItemViewModel item, string dependencyId, bool use)
    {
        var dep = item.ShownDependencies.FirstOrDefault(d => d.Id == dependencyId);
        if (dep is not { Optional: true }) return;   // Task 6 (e): a required dependency can't be skipped
        if (_ws.Session.IsBusy)                       // fix round M3: files are in use while the game runs
        {
            Status = $"{item.Name}: close the game to change its dependencies";
            DependencyWork = item.RefreshDependenciesAsync();
            return;
        }

        var key = $"{item.Entry.Id}/{dependencyId}";
        var skipped = _ws.Client.SkippedDependencies;
        if (use) skipped.RemoveAll(s => s == key);
        else if (!skipped.Contains(key)) skipped.Add(key);
        _ws.SaveProfile();

        DependencyWork = use ? UseAsync(item, dep) : UnuseAsync(item, dep);
    }

    /// <summary>Un-using removes the dependency at once — and every dependency that requires it (directly
    /// or through another), since those would be skipped at the next launch anyway. Awaited: the removal waits
    /// for the game folder's dependency gate, never blocking the UI thread on it.</summary>
    private async Task UnuseAsync(PluginItemViewModel item, PluginDependency dep)
    {
        var gone = new HashSet<string> { dep.Id };
        foreach (var d in item.ShownDependencies)   // manifest order: a prerequisite is listed before its dependents
            if (d.Requires?.Any(gone.Contains) == true) gone.Add(d.Id);
        var svc = _ws.Services.Core.Install.Dependencies;
        var gameMini = _ws.Client.GameMiniDir;
        try
        {
            foreach (var id in gone) await svc.RemoveAsync(gameMini, item.Entry.Id, id);
            Status = $"{item.Name}: {dep.Name} skipped";
        }
        catch (Exception ex) { Status = $"{item.Name}: {dep.Name} could not be removed — {ex.Message}"; }
        await item.RefreshDependenciesAsync();
    }

    private async Task UseAsync(PluginItemViewModel item, PluginDependency dep)
    {
        await item.RefreshDependenciesAsync();
        await EnsureNowAsync(item, dep);
    }

    /// <summary>Re-using a dependency of an installed plugin on a Modded client installs it now, off the UI
    /// thread, reporting through the status line; otherwise (Vanilla, or the game is running) it waits for
    /// the next Modded launch (the page says so). Never throws.</summary>
    private async Task EnsureNowAsync(PluginItemViewModel item, PluginDependency dep)
    {
        var client = _ws.Client;
        if (_ws.Session.IsBusy || !client.Modded || item.InstalledVersion is not { } iv || item.ShownVersion?.Version != iv) return;
        var svc = _ws.Services.Core.Install.Dependencies;
        var deps = item.ShownDependencies;
        var skippedIds = DependencyRunner.Skipped(client, item.Entry.Id, deps);
        Status = $"Preparing {item.Name}: {dep.Name}…";
        try
        {
            var results = await Task.Run(async () =>
            {
                try { await svc.UnparkModdedOnlyAsync(client.GameMiniDir); } catch (Exception) { /* fail-open, see PluginDownloads */ }
                return await svc.EnsureAsync(client.GameMiniDir, item.Entry.Id, deps, skippedIds, CancellationToken.None);
            });
            var problems = results.Where(s => s.State is DependencyState.Blocked or DependencyState.Failed)
                .Select(s => DependencyRunner.Line(item.Entry.Id, s)).ToList();
            Status = problems.Count == 0 ? $"{item.Name}: {dep.Name} installed" : $"{item.Name}: {string.Join("; ", problems)}";
        }
        catch (Exception ex) { Status = $"{item.Name}: {dep.Name} failed — {ex.Message}"; }
        await item.RefreshDependenciesAsync();
    }
}

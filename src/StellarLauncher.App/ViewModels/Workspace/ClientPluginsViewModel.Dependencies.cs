using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.App.Services;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;

namespace StellarLauncher.App.ViewModels.Workspace;

/// <summary>The plugin page's DEPENDENCIES section (Task 6): status of the shown version's dependencies on
/// this client, and the player's choice to use or skip an OPTIONAL one (stored as "&lt;plugin&gt;/&lt;dep&gt;"
/// in <see cref="Core.Clients.ClientProfile.SkippedDependencies"/>).</summary>
public sealed partial class ClientPluginsViewModel
{
    /// <summary>The most recent background dependency install started by <see cref="SetDependencyUse"/>
    /// (completed when none is running) — awaited by tests.</summary>
    public Task DependencyWork { get; private set; } = Task.CompletedTask;

    /// <summary>The read-only status (Installed / NotInstalled / Skipped / Blocked) — except that a dependency
    /// still NotInstalled whose last install attempt this session Failed (same declaration since) shows that
    /// failure instead, so "Failed: …" reaches the page (Status itself is offline and never fails a download).</summary>
    public IReadOnlyList<DependencyStatus> DependencyStatus(PluginItemViewModel item)
    {
        var deps = item.ShownDependencies;
        var install = _ws.Services.Core.Install;
        var gameMini = _ws.Client.GameMiniDir;
        var statuses = install.Dependencies.Status(gameMini, item.Entry.Id, deps, DependencyRunner.Skipped(_ws.Client, item.Entry.Id, deps));
        if (install.Outcomes is not { } outcomes) return statuses;
        return statuses.Select(s =>
            s.State == DependencyState.NotInstalled && deps.FirstOrDefault(d => d.Id == s.DependencyId) is { } d
            && outcomes.LastFailure(gameMini, item.Entry.Id, d) is { } failed ? failed : s).ToList();
    }

    public void SetDependencyUse(PluginItemViewModel item, string dependencyId, bool use)
    {
        var dep = item.ShownDependencies.FirstOrDefault(d => d.Id == dependencyId);
        if (dep is not { Optional: true }) return;   // Task 6 (e): a required dependency can't be skipped

        var key = $"{item.Entry.Id}/{dependencyId}";
        var skipped = _ws.Client.SkippedDependencies;
        if (use) skipped.RemoveAll(s => s == key);
        else if (!skipped.Contains(key)) skipped.Add(key);
        _ws.SaveProfile();

        if (!use) RemoveNow(item, dep);
        item.RefreshDependencies();
        if (use) DependencyWork = EnsureNowAsync(item, dep);
    }

    /// <summary>Un-using removes the dependency at once — and every dependency that requires it (directly
    /// or through another), since those would be skipped at the next launch anyway.</summary>
    private void RemoveNow(PluginItemViewModel item, PluginDependency dep)
    {
        var gone = new HashSet<string> { dep.Id };
        foreach (var d in item.ShownDependencies)   // manifest order: a prerequisite is listed before its dependents
            if (d.Requires?.Any(gone.Contains) == true) gone.Add(d.Id);
        try
        {
            foreach (var id in gone) _ws.Services.Core.Install.Dependencies.Remove(_ws.Client.GameMiniDir, item.Entry.Id, id);
            Status = $"{item.Name}: {dep.Name} skipped";
        }
        catch (Exception ex) { Status = $"{item.Name}: {dep.Name} could not be removed — {ex.Message}"; }
    }

    /// <summary>Re-using a dependency of an installed plugin on a Modded client installs it now, off the UI
    /// thread, reporting through the status line; otherwise it waits for the next Modded launch (the page
    /// says so). Never throws.</summary>
    private async Task EnsureNowAsync(PluginItemViewModel item, PluginDependency dep)
    {
        var client = _ws.Client;
        if (!client.Modded || item.InstalledVersion is not { } iv || item.ShownVersion?.Version != iv) return;
        var svc = _ws.Services.Core.Install.Dependencies;
        var deps = item.ShownDependencies;
        var skippedIds = DependencyRunner.Skipped(client, item.Entry.Id, deps);
        Status = $"Preparing {item.Name}: {dep.Name}…";
        try
        {
            var results = await Task.Run(async () =>
            {
                try { svc.UnparkModdedOnly(client.GameMiniDir); } catch (Exception) { /* fail-open, see PluginDownloads */ }
                return await svc.EnsureAsync(client.GameMiniDir, item.Entry.Id, deps, skippedIds, CancellationToken.None);
            });
            var problems = results.Where(s => s.State is DependencyState.Blocked or DependencyState.Failed)
                .Select(s => DependencyRunner.Line(item.Entry.Id, s)).ToList();
            Status = problems.Count == 0 ? $"{item.Name}: {dep.Name} installed" : $"{item.Name}: {string.Join("; ", problems)}";
        }
        catch (Exception ex) { Status = $"{item.Name}: {dep.Name} failed — {ex.Message}"; }
        item.RefreshDependencies();
    }
}

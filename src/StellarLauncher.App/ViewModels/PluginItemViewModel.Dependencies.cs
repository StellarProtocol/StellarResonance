using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;

namespace StellarLauncher.App.ViewModels;

/// <summary>The plugin page's DEPENDENCIES section (Task 6).</summary>
public partial class PluginItemViewModel
{
    private int _refreshGeneration;   // UI thread: only the newest refresh may apply its rows

    public ObservableCollection<DependencyItemViewModel> Dependencies { get; } = new();
    public bool HasDependencies => Dependencies.Count > 0;
    public IEnumerable<string> DependencyNotices =>
        Dependencies.Where(d => d.HasNotice).Select(d => $"Notice from {Name}: {d.Notice!.Trim()}").Distinct();
    public bool HasOptionalDependency => Dependencies.Any(d => d.IsOptional);
    public string DependencyFootnote => HasOptionalDependency
        ? $"Untick an optional dependency to skip it; {Name} works without it. Removing the plugin removes everything listed here."
        : "Removing the plugin removes everything listed here.";

    /// <summary>Fix round M3: true while this client's game is running — the checkboxes are disabled and
    /// no dependency is installed or removed (files in use).</summary>
    [ObservableProperty] private bool _dependenciesLocked;
    partial void OnDependenciesLockedChanged(bool value) { foreach (var r in Dependencies) r.Locked = value; }

    /// <summary>The most recent <see cref="RefreshDependenciesAsync"/> (completed when idle) — awaited by tests.</summary>
    public Task DependencyRefresh { get; private set; } = Task.CompletedTask;

    /// <summary>The page shows the dependencies of the INSTALLED version when the registry still lists it,
    /// else of the newest version (what an install would bring).</summary>
    public PluginVersion? ShownVersion =>
        (InstalledVersion is { } iv ? Versions.FirstOrDefault(v => v.Version == iv) : null) ?? Versions.FirstOrDefault();
    public IReadOnlyList<PluginDependency> ShownDependencies =>
        ShownVersion?.Dependencies ?? (IReadOnlyList<PluginDependency>)Array.Empty<PluginDependency>();

    /// <summary>Rebuilds the section from <see cref="ShownDependencies"/> and the host's status. Fix round M1:
    /// the status (ledger reads + file hashing) is computed OFF the UI thread; the rows are applied back on
    /// the caller's context. Rows whose ids are unchanged are updated in place (so a checkbox mid-toggle
    /// keeps its row). A failing status read leaves the section as it was.</summary>
    public Task RefreshDependenciesAsync() => DependencyRefresh = RefreshCoreAsync();

    private async Task RefreshCoreAsync()
    {
        var generation = ++_refreshGeneration;
        var deps = ShownDependencies;
        IReadOnlyList<DependencyStatus> statuses;
        try { statuses = deps.Count == 0 ? Array.Empty<DependencyStatus>() : await Task.Run(() => _parent.DependencyStatus(this)); }
        catch (Exception) { return; }
        if (generation != _refreshGeneration) return;   // a newer refresh started meanwhile; it owns the rows
        ApplyDependencyRows(deps, statuses);
    }

    private void ApplyDependencyRows(IReadOnlyList<PluginDependency> deps, IReadOnlyList<DependencyStatus> statuses)
    {
        var byId = statuses.ToDictionary(s => s.DependencyId);
        DependencyStatus StatusOf(PluginDependency d) =>
            byId.TryGetValue(d.Id, out var s) ? s : new DependencyStatus(d.Id, DependencyState.NotInstalled, null);

        if (Dependencies.Select(r => r.Id).SequenceEqual(deps.Select(d => d.Id)))
        {
            for (var i = 0; i < deps.Count; i++)
            {
                var st = StatusOf(deps[i]);
                Dependencies[i].Update(st, st.State != DependencyState.Skipped);
            }
        }
        else
        {
            Dependencies.Clear();
            foreach (var d in deps)
            {
                var st = StatusOf(d);
                Dependencies.Add(new DependencyItemViewModel(d, st, st.State != DependencyState.Skipped,
                    (id, use) => _parent.SetDependencyUse(this, id, use), RequiresLabel(d, deps)) { Locked = DependenciesLocked });
            }
        }
        OnPropertyChanged(nameof(HasDependencies));
        OnPropertyChanged(nameof(DependencyNotices));
        OnPropertyChanged(nameof(DependencyFootnote));
    }

    /// <summary>Fix round M4: "with &lt;prerequisite names&gt;" (the mockup's chip), or null without <c>requires</c>.</summary>
    private static string? RequiresLabel(PluginDependency d, IReadOnlyList<PluginDependency> all) =>
        d.Requires is { Count: > 0 } req
            ? "with " + string.Join(", ", req.Select(id => all.FirstOrDefault(x => x.Id == id)?.Name ?? id))
            : null;
}

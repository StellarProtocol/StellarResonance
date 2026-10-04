using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        ? $"Untick an optional dependency to skip it; {Name} works without it. Removing {Name} asks whether to remove these too."
        : $"Removing {Name} asks whether to remove these too.";

    /// <summary>v3 V3: this plugin's dependencies were kept when it was removed — still launcher-managed.</summary>
    [ObservableProperty] private bool _dependenciesKept;
    partial void OnDependenciesKeptChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDependencySection));
        OnPropertyChanged(nameof(KeptNote));
    }

    /// <summary>The section shows for declared dependencies, and for kept ones even when the shown version declares none.</summary>
    public bool ShowDependencySection => HasDependencies || DependenciesKept;

    /// <summary>Mockup v3 "removed, dependency kept" wording; the Vanilla clause only when something kept is moddedOnly.</summary>
    public string KeptNote => Dependencies.Any(d => d.Dependency.ModdedOnly)
        ? $"Kept after {Name} was removed. Still moved aside for Vanilla launches; reinstalling {Name} uses it again."
        : $"Kept after {Name} was removed. Reinstalling {Name} uses it again.";

    [RelayCommand] private Task RemoveKeptDependencies() => _parent.RemoveKeptDependenciesAsync(this);

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
        var shownDeps = ShownDependencies;
        // Fix round M1 still holds: CALL (not yet await) DependencyStatusAsync first, exactly as before — its
        // implementation must snapshot UI-thread state (e.g. SkippedDependencies) synchronously, before this method's
        // own first await lets the UI thread run further. Awaiting DependenciesKeptAsync FIRST would delay that call
        // past the point the UI could have already mutated that state.
        var statusTask = shownDeps.Count == 0 ? Task.FromResult<IReadOnlyList<DependencyStatus>>(Array.Empty<DependencyStatus>()) : _parent.DependencyStatusAsync(this);
        var keptTask = _parent.DependenciesKeptAsync(this);

        bool kept;
        try { kept = await keptTask; }
        catch (Exception) { kept = false; }

        IReadOnlyList<PluginDependency> deps;
        IReadOnlyList<DependencyStatus> statuses;
        if (kept)
        {
            // Review fix round 3 (4): the kept section shows what the LEDGER actually holds, not what the
            // currently shown version happens to declare (which may have changed, or dropped it entirely).
            // statusTask (started above) is irrelevant here and deliberately left unobserved.
            IReadOnlyList<LedgerEntry> entries;
            try { entries = await _parent.KeptLedgerEntriesAsync(this); }
            catch (Exception) { entries = Array.Empty<LedgerEntry>(); }
            deps = entries.Select(ResolveKeptDependency).ToList();
            statuses = deps.Select(d => new DependencyStatus(d.Id, DependencyState.Installed, null)).ToList();
        }
        else
        {
            deps = shownDeps;
            try { statuses = await statusTask; }
            catch (Exception) { return; }
        }

        if (generation != _refreshGeneration) return;   // a newer refresh started meanwhile; it owns the rows
        ApplyDependencyRows(deps, statuses);
        DependenciesKept = kept;
    }

    /// <summary>Review fix round 3 (4): resolves one kept ledger entry to a displayable dependency — the manifest
    /// name/metadata when SOME version of this plugin still declares the id, else a minimal synthetic dependency
    /// naming just the bare id, with ModdedOnly read from the ledger entry's OWN recorded files (never from a
    /// manifest that no longer names it).</summary>
    private PluginDependency ResolveKeptDependency(LedgerEntry entry)
    {
        var known = Entry.Versions.SelectMany(v => v.Dependencies ?? Array.Empty<PluginDependency>())
            .FirstOrDefault(d => d.Id == entry.DependencyId);
        if (known is not null) return known;
        return new PluginDependency(entry.DependencyId, entry.DependencyId, entry.Version, "", "", 0, "file",
            Array.Empty<PluginDependencyFile>(), "game", ModdedOnly: entry.Files.Any(f => f.ModdedOnly), License: "");
    }

    private void ApplyDependencyRows(IReadOnlyList<PluginDependency> deps, IReadOnlyList<DependencyStatus> statuses)
    {
        var byId = statuses.ToDictionary(s => s.DependencyId);
        DependencyStatus StatusOf(PluginDependency d) =>
            byId.TryGetValue(d.Id, out var s) ? Named(s, deps) : new DependencyStatus(d.Id, DependencyState.NotInstalled, null);

        if (Dependencies.Select(r => r.Id).SequenceEqual(deps.Select(d => d.Id)))
        {
            for (var i = 0; i < deps.Count; i++)
            {
                var st = StatusOf(deps[i]);
                Dependencies[i].Update(st, Used(st));
            }
        }
        else
        {
            Dependencies.Clear();
            foreach (var d in deps)
            {
                var st = StatusOf(d);
                Dependencies.Add(new DependencyItemViewModel(d, st, Used(st),
                    (id, use) => _parent.SetDependencyUse(this, id, use), RequiresLabel(d, deps)) { Locked = DependenciesLocked });
            }
        }
        OnPropertyChanged(nameof(HasDependencies));
        OnPropertyChanged(nameof(DependencyNotices));
        OnPropertyChanged(nameof(DependencyFootnote));
        OnPropertyChanged(nameof(ShowDependencySection));
        OnPropertyChanged(nameof(KeptNote));
    }

    /// <summary>Ticked unless the player skipped it — a dependency only WAITING for a blocked prerequisite is
    /// still wanted (M-d).</summary>
    private static bool Used(DependencyStatus s) =>
        s.State != DependencyState.Skipped || s.Reason == DependencyReason.WaitingForPrerequisite;

    /// <summary>M-d: a waiting status carries the prerequisite's id; the row shows its name.</summary>
    private static DependencyStatus Named(DependencyStatus s, IReadOnlyList<PluginDependency> deps) =>
        s.Reason == DependencyReason.WaitingForPrerequisite && deps.FirstOrDefault(x => x.Id == s.Detail) is { } p
            ? s with { Detail = p.Name } : s;

    /// <summary>Fix round M4: "with &lt;prerequisite names&gt;" (the mockup's chip), or null without <c>requires</c>.</summary>
    private static string? RequiresLabel(PluginDependency d, IReadOnlyList<PluginDependency> all) =>
        d.Requires is { Count: > 0 } req
            ? "with " + string.Join(", ", req.Select(id => all.FirstOrDefault(x => x.Id == id)?.Name ?? id))
            : null;
}

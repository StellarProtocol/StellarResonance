using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StellarLauncher.App.Services;
using StellarLauncher.Core.Dependencies;

namespace StellarLauncher.App.ViewModels.Workspace;

/// <summary>v3 V3 (spec § 12): removing a plugin with dependencies, and the dependencies kept afterwards.</summary>
public sealed partial class ClientPluginsViewModel
{
    /// <summary>A plugin with a dependency ledger here asks first (remove them too — the default — or the plugin only;
    /// Cancel removes nothing). Without a ledger there is nothing to choose: removed at once, as before. Fix round 1,
    /// Minor 2 still holds: dependency cleanup runs BEFORE the plugin DLL goes, a failed cleanup still removes the plugin
    /// but keeps its skip entries. "Plugin only" marks the ledger kept first — if that fails nothing is removed, or the
    /// next launch's orphan sweep would delete what the player chose to keep. Fix round 3 (1): if the plugin itself then
    /// fails to remove, the kept mark is best-effort reverted — the plugin is still installed, so its dependencies must
    /// not be left recorded as kept for a removal that never happened. Fix round 3 (2) — controller decision: "plugin
    /// only" KEEPS the plugin's SkippedDependencies (a reinstall pre-fills the earlier opt-out via
    /// <see cref="PluginStepBuilder.Install"/>'s client parameter); only "plugin and dependencies" clears them.</summary>
    public async Task RemoveAsync(PluginItemViewModel item)
    {
        var choice = RemoveChoice.PluginAndDependencies;
        if (await RemoveStepForAsync(item) is { } step)
        {
            if (await _ws.Services.Core.Steps.AskRemoveAsync(step) is not { } picked) return;
            choice = picked;
        }
        try
        {
            string? depFailure;
            if (choice == RemoveChoice.PluginOnly)
            {
                if (await TryDependencyWorkAsync(svc => svc.SetKeptAsync(_ws.Client.GameMiniDir, item.Entry.Id, true)) is { } keepFailure)
                {
                    Status = $"{item.Name}: its dependencies could not be kept — nothing was removed ({keepFailure})";
                    return;
                }
                depFailure = null;
                try { _ws.Services.Core.Install.Plugins.Remove(_ws.Client.GameMiniDir, item.Entry.Id, item.CanonicalDll); }
                catch
                {
                    // The plugin is STILL installed — don't leave its ledger marked kept for a removal that never happened.
                    await TryDependencyWorkAsync(svc => svc.SetKeptAsync(_ws.Client.GameMiniDir, item.Entry.Id, false));
                    throw;
                }
            }
            else
            {
                depFailure = await TryDependencyWorkAsync(svc => svc.RemoveAllAsync(_ws.Client.GameMiniDir, item.Entry.Id));
                _ws.Services.Core.Install.Plugins.Remove(_ws.Client.GameMiniDir, item.Entry.Id, item.CanonicalDll);
            }

            if (choice != RemoveChoice.PluginOnly && depFailure is null)
            {
                _ws.Client.SkippedDependencies.RemoveAll(s => s.StartsWith(item.Entry.Id + "/", StringComparison.Ordinal));
                _ws.SaveProfile();
            }
            item.MarkRemoved();
            Status = choice == RemoveChoice.PluginOnly ? $"{item.Name}: removed — its dependencies were kept"
                : depFailure is null ? $"{item.Name}: removed"
                : $"{item.Name}: removed (dependency cleanup failed — {depFailure})";
        }
        catch (Exception ex) { Status = $"{item.Name} failed: {ex.Message}"; }
        await _ws.RefreshAsync();
    }

    /// <summary>Awaited (never a blocking wait — the folder's gate may be held by a download), failure as text.</summary>
    private async Task<string?> TryDependencyWorkAsync(Func<Core.Dependencies.IDependencyService, Task> work)
    {
        try { await work(_ws.Services.Core.Install.Dependencies); return null; }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>The remove step, or null when this plugin has no dependency ledger here, or declares no dependencies at
    /// all right now (review fix round 1 (a): never show a step with an empty names list — PluginStepBuilder.Remove's
    /// "the dependencies" placeholder is for an all-skipped-but-declared ledger, not for nothing declared). Listing is
    /// read off the UI thread; a failed read = no step, i.e. the previous behaviour.</summary>
    private async Task<RemoveStep?> RemoveStepForAsync(PluginItemViewModel item)
    {
        if (item.ShownDependencies.Count == 0) return null;
        var (svc, gameMini, id) = (_ws.Services.Core.Install.Dependencies, _ws.Client.GameMiniDir, item.Entry.Id);
        bool hasLedger;
        try { hasLedger = await Task.Run(() => svc.LedgerPluginIds(gameMini).Contains(id)); }
        catch (Exception) { hasLedger = false; }
        return hasLedger ? PluginStepBuilder.Remove(_ws.Client, item.Entry, item.ShownDependencies) : null;
    }

    public Task<bool> DependenciesKeptAsync(PluginItemViewModel item)
    {
        var (svc, gameMini, id) = (_ws.Services.Core.Install.Dependencies, _ws.Client.GameMiniDir, item.Entry.Id);
        return Task.Run(() => svc.IsKept(gameMini, id));
    }

    public Task<IReadOnlyList<LedgerEntry>> KeptLedgerEntriesAsync(PluginItemViewModel item)
    {
        var (svc, gameMini, id) = (_ws.Services.Core.Install.Dependencies, _ws.Client.GameMiniDir, item.Entry.Id);
        return Task.Run(() => svc.LedgerEntries(gameMini, id));
    }

    public Task<IReadOnlyDictionary<string, KeptDependencyDiskState>> KeptDiskStatesAsync(PluginItemViewModel item)
    {
        var (svc, gameMini, id) = (_ws.Services.Core.Install.Dependencies, _ws.Client.GameMiniDir, item.Entry.Id);
        return Task.Run(() => (IReadOnlyDictionary<string, KeptDependencyDiskState>)svc.LedgerEntries(gameMini, id)
            .ToDictionary(e => e.DependencyId, e => svc.KeptDiskState(gameMini, id, e.DependencyId)));
    }

    public async Task RemoveKeptDependenciesAsync(PluginItemViewModel item)
    {
        if (_ws.Session.IsBusy) { Status = $"{item.Name}: close the game to change its dependencies"; return; }   // files in use
        Status = await TryDependencyWorkAsync(svc => svc.RemoveAllAsync(_ws.Client.GameMiniDir, item.Entry.Id)) is { } failure
            ? $"{item.Name}: kept dependencies could not be removed — {failure}"
            : $"{item.Name}: kept dependencies removed";
        await item.RefreshDependenciesAsync();
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.App.Services;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Matrix;

namespace StellarLauncher.App.ViewModels.Workspace;

public enum PluginFilter { All, Installed, Updates, Disabled }

/// <summary>This client's plugins (its folder is the target), every other client in view (mockup #plugins).</summary>
public sealed partial class ClientPluginsViewModel : ObservableObject, IPluginActions
{
    private readonly ClientWorkspaceViewModel _ws;
    private readonly List<PluginRowViewModel> _all = new();
    private readonly List<ClientColumn> _others = new();

    public ObservableCollection<PluginRowViewModel> Rows { get; } = new();
    public ObservableCollection<ClientProfile> CopySources { get; } = new();
    [ObservableProperty] private PluginFilter _filter = PluginFilter.All;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _gridView;               // false = row list, true = card grid (persisted launcher-wide)
    [ObservableProperty] private PluginItemViewModel? _selectedPlugin;
    [ObservableProperty] private Bitmap? _lightboxImage;

    public int InstalledCount => _all.Count(r => r.IsInstalled);
    public int UpdateCount => _all.Count(r => r.HasUpdate);
    public int DisabledCount => _all.Count(r => r.IsDisabled);
    public bool IsDetailOpen => SelectedPlugin is not null;
    public bool IsLightboxOpen => LightboxImage is not null;
    public string TargetLine => $"installing to {System.IO.Path.Combine(_ws.Client.GameMiniDir, "stellar", "plugins")} · framework {_ws.Inventory.FrameworkVersion ?? "none"} · registry: {(_ws.IsTesting ? "testing over stable" : "stable")}";
    public bool IsAll => Filter == PluginFilter.All; public bool IsInstalledFilter => Filter == PluginFilter.Installed;
    public bool IsUpdatesFilter => Filter == PluginFilter.Updates; public bool IsDisabledFilter => Filter == PluginFilter.Disabled;
    public bool IsListView => !GridView;

    public ClientPluginsViewModel(ClientWorkspaceViewModel ws)
    {
        _ws = ws;
        _gridView = ws.Shell.Config.Launcher.PluginsGridView;
        _ws.Refreshed += () => _ = ReloadAsync();   // released by the workspace's Dispose (Refreshed = null)
        _ = ReloadAsync();
    }

    partial void OnGridViewChanged(bool value)
    {
        OnPropertyChanged(nameof(IsListView));
        _ws.Shell.Config.Launcher.PluginsGridView = value;
        _ws.Shell.SaveConfig();
    }

    [RelayCommand] private void ShowList() => GridView = false;
    [RelayCommand] private void ShowGrid() => GridView = true;

    [RelayCommand]
    public async Task ReloadAsync()
    {
        var self = new ClientColumn(_ws.Client, _ws.Inventory, _ws.Registry);
        // Gather the other clients into a local list first: the awaits below can interleave with a second Reload
        // (an install triggers Refreshed), and clearing shared lists before awaiting would double-add.
        var others = new List<ClientColumn>();
        foreach (var other in _ws.Shell.Config.Clients.Where(c => c.Id != _ws.Client.Id))
        {
            var reg = await _ws.Services.Core.Registry.ForChannelAsync(other.Channel, CancellationToken.None);
            others.Add(new ClientColumn(other, _ws.Services.Core.Inventory.Read(other, reg), reg));
        }
        _others.Clear(); _others.AddRange(others);
        CopySources.Clear(); foreach (var o in others) CopySources.Add(o.Client);
        SelectedPlugin = null;
        _all.Clear();
        var i = 0;
        foreach (var e in _ws.Registry.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            var inv = _ws.Inventory.Plugins.FirstOrDefault(p => p.Entry.Id == e.Id);
            var item = new PluginItemViewModel(e, inv?.Present ?? false, inv?.Version, _ws.Inventory.FrameworkVersion, this) { IsDisabled = inv?.Disabled ?? false };
            _all.Add(new PluginRowViewModel(item, PluginMatrixBuilder.Classify(self, e.Id), AlsoOnFor(e.Id), i++, this));
        }
        ApplyFilter();
        foreach (var r in _all) _ = r.Item.LoadThumbnailAsync(_ws.Services.Core.Install.Http);   // swallows its own failures
        OnPropertyChanged(nameof(TargetLine));
    }

    private IEnumerable<AlsoOnChip> AlsoOnFor(string pluginId)
    {
        var chips = _others.Select(col =>
        {
            var p = col.Inventory.Plugins.FirstOrDefault(x => x.Entry.Id == pluginId);
            return p is { Present: true }
                ? new AlsoOnChip($"{col.Client.Name} {p.Version ?? "?"}{(p.Disabled ? " · off" : "")}", true)
                : new AlsoOnChip($"{col.Client.Name} –", false);
        }).ToList();
        return chips.Count <= 4 ? chips : chips.Take(3).Append(new AlsoOnChip($"+{chips.Count - 3}", false));
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        var q = Search.Trim();
        foreach (var r in _all.Where(r => Filter switch
                 {
                     PluginFilter.Installed => r.IsInstalled,
                     PluginFilter.Updates => r.HasUpdate,
                     PluginFilter.Disabled => r.IsDisabled,
                     _ => true,
                 }).Where(r => q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
            Rows.Add(r);
        foreach (var p in new[] { nameof(InstalledCount), nameof(UpdateCount), nameof(DisabledCount), nameof(IsAll), nameof(IsInstalledFilter), nameof(IsUpdatesFilter), nameof(IsDisabledFilter) })
            OnPropertyChanged(p);
    }

    partial void OnSearchChanged(string value) => ApplyFilter();
    [RelayCommand] private void SetFilter(PluginFilter f) { Filter = f; ApplyFilter(); }

    // ---- actions on THIS client's folder ----
    public async Task InstallVersionAsync(PluginRowViewModel row, string? version)
    {
        var v = row.Item.Entry.Versions.FirstOrDefault(x => x.Version == version) ?? row.Item.SelectedVersion;
        if (v is null) return;
        try { await PluginDownloads.InstallAsync(_ws.Services.Core.Install, _ws.Client.GameMiniDir, row.Item.Entry, v, s => Status = $"{row.Name}: {s}"); }
        catch (Exception ex) { Status = $"{row.Name} failed: {ex.Message}"; }
        await _ws.RefreshAsync();
    }

    public async Task SetEnabledAsync(PluginRowViewModel row, bool enabled)
    {
        try
        {
            if (enabled) _ws.Services.Core.Install.Plugins.Enable(_ws.Client.GameMiniDir, row.Item.Entry.Id);
            else _ws.Services.Core.Install.Plugins.Disable(_ws.Client.GameMiniDir, row.Item.Entry.Id);
            Status = $"{row.Name}: {(enabled ? "enabled" : "disabled")} — applies on the next launch";
        }
        catch (Exception ex) { Status = $"{row.Name} failed: {ex.Message}"; }   // fire-and-forget from the toggle
        await _ws.RefreshAsync();
    }

    public Task InstallAsync(PluginItemViewModel item) => InstallVersionAsync(RowOf(item), item.SelectedVersion?.Version);
    public async Task RemoveAsync(PluginItemViewModel item)
    {
        try
        {
            _ws.Services.Core.Install.Plugins.Remove(_ws.Client.GameMiniDir, item.Entry.Id, item.CanonicalDll);
            item.MarkRemoved(); Status = $"{item.Name}: removed";
        }
        catch (Exception ex) { Status = $"{item.Name} failed: {ex.Message}"; }
        await _ws.RefreshAsync();
    }
    public Task EnableAsync(PluginItemViewModel item) => SetEnabledAsync(RowOf(item), true);
    private PluginRowViewModel RowOf(PluginItemViewModel item) => _all.First(r => ReferenceEquals(r.Item, item));

    [RelayCommand]
    private async Task CopySetFrom(ClientProfile source)
    {
        var src = _others.FirstOrDefault(c => c.Client.Id == source.Id);
        if (src is null) return;
        var plan = CopySetPlanner.Plan(src, new ClientColumn(_ws.Client, _ws.Inventory, _ws.Registry));
        var installs = plan.Where(i => i.Status == CopyStatus.Install).ToList();
        if (installs.Count == 0) { Status = $"nothing to copy from {source.Name}"; return; }
        var body = string.Join("\n", plan.Select(i => $"{i.Entry.Name}: {(i.Status == CopyStatus.Install ? $"install v{i.TargetVersion}" : i.Status.ToString())}"));
        if (!await _ws.Services.Confirm.AskAsync($"Copy plugin set from {source.Name} to {_ws.Client.Name}?", body, $"Install {installs.Count}")) return;
        var ok = 0;
        foreach (var i in installs)
        {
            var v = i.Entry.Versions.First(x => x.Version == i.TargetVersion);
            try { await PluginDownloads.InstallAsync(_ws.Services.Core.Install, _ws.Client.GameMiniDir, i.Entry, v, null); ok++; }
            catch (Exception ex) { Status = $"{i.Entry.Name} failed: {ex.Message}"; }
        }
        Status = $"copied from {source.Name}: {ok} installed, {plan.Count - installs.Count} skipped";
        await _ws.RefreshAsync();
    }

    // ---- detail page + lightbox (same member names as the old PluginsViewModel so the XAML moves verbatim) ----
    partial void OnSelectedPluginChanged(PluginItemViewModel? value) => OnPropertyChanged(nameof(IsDetailOpen));
    partial void OnLightboxImageChanged(Bitmap? value) => OnPropertyChanged(nameof(IsLightboxOpen));
    [RelayCommand] private void OpenPlugin(PluginItemViewModel item) { SelectedPlugin = item; _ = item.EnsureDetailLoadedAsync(_ws.Services.Core.Install.Http, bmp => LightboxImage = bmp); }
    [RelayCommand] private void CloseDetail() => SelectedPlugin = null;
    [RelayCommand] private void CloseLightbox() { var old = LightboxImage; LightboxImage = null; old?.Dispose(); }
    [RelayCommand] private void OpenLink(string? url) => Services.Browser.Open(url);
}

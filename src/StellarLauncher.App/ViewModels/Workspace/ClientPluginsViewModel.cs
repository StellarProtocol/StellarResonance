using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.App.Localization;
using StellarLauncher.App.Services;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Matrix;

namespace StellarLauncher.App.ViewModels.Workspace;

public enum PluginFilter { All, Installed, Updates, Disabled }

/// <summary>This client's plugins (its folder is the target), every other client in view (mockup #plugins).</summary>
public sealed partial class ClientPluginsViewModel : ObservableObject, IPluginActions, IDisposable
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
    public string InstalledFilterLabel => Loc.TFormat("plugins.filter.installed", InstalledCount);
    public string UpdatesFilterLabel => Loc.TFormat("plugins.filter.updates", UpdateCount);
    public string DisabledFilterLabel => Loc.TFormat("plugins.filter.disabled", DisabledCount);
    public string TargetLine => Loc.TFormat("plugins.target", System.IO.Path.Combine(_ws.Client.GameMiniDir, "stellar", "plugins"),
        _ws.Inventory.FrameworkVersion ?? Loc.T("common.none"), _ws.IsTesting ? Loc.T("plugins.registry.testing") : Loc.T("channel.stable"));
    public bool IsAll => Filter == PluginFilter.All; public bool IsInstalledFilter => Filter == PluginFilter.Installed;
    public bool IsUpdatesFilter => Filter == PluginFilter.Updates; public bool IsDisabledFilter => Filter == PluginFilter.Disabled;
    public bool IsListView => !GridView;

    public ClientPluginsViewModel(ClientWorkspaceViewModel ws)
    {
        _ws = ws;
        _gridView = ws.Shell.Config.Launcher.PluginsGridView;
        _ws.Refreshed += () => _ = ReloadAsync();   // released by the workspace's Dispose (Refreshed = null)
        _onSession = _ => { if (SelectedPlugin is { } p) p.DependenciesLocked = _ws.Session.IsBusy; };
        _ws.Session.Changed += _onSession;          // released by Dispose (the workspace disposes its tab VMs)
        _ = ReloadAsync();
        Loc.Subscribe(this, vm => vm.OnPropertyChanged(string.Empty));
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
        // Fire-and-forget from the ctor and Refreshed, so it must observe its own exceptions (offline registry/read).
        try
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
            // NOTE: don't clear Status on success — a reload is triggered after install/copy, whose result message must survive.
        }
        catch (Exception ex) { Status = Loc.TFormat("ws.offline", ex.Message); }
    }

    // i18n: chip text is built at load time — NOT live on a language switch (page rebuilt on navigation).
    private IEnumerable<AlsoOnChip> AlsoOnFor(string pluginId)
    {
        var chips = _others.Select(col =>
        {
            var p = col.Inventory.Plugins.FirstOrDefault(x => x.Entry.Id == pluginId);
            return p is { Present: true }
                ? new AlsoOnChip(p.Disabled ? Loc.TFormat("plugins.alsoOn.off", col.Client.Name, p.Version ?? "?") : $"{col.Client.Name} {p.Version ?? "?"}", true)
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
        foreach (var p in new[] { nameof(InstalledCount), nameof(UpdateCount), nameof(DisabledCount),
                     nameof(InstalledFilterLabel), nameof(UpdatesFilterLabel), nameof(DisabledFilterLabel), nameof(IsAll), nameof(IsInstalledFilter), nameof(IsUpdatesFilter), nameof(IsDisabledFilter) })
            OnPropertyChanged(p);
    }

    partial void OnSearchChanged(string value) => ApplyFilter();
    [RelayCommand] private void SetFilter(PluginFilter f) { Filter = f; ApplyFilter(); }

    // ---- actions on THIS client's folder ----
    public async Task InstallVersionAsync(PluginRowViewModel row, string? version)
    {
        var v = row.Item.Entry.Versions.FirstOrDefault(x => x.Version == version) ?? row.Item.SelectedVersion;
        if (v is null) return;
        var request = new PluginInstallRequest(_ws.Client, row.Item.Entry, v, row.Item.Installed, row.Item.InstalledVersion);
        try
        {
            // v3: Cancel in the step changes nothing — no refresh either.
            if (!await PluginInstallFlow.RunAsync(_ws.Services.Core.Install, _ws.Services.Core.Steps, request, _ws.SaveProfile,
                    s => Status = Loc.TFormat("plugins.status", row.Name, s))) return;
        }
        catch (Exception ex) { Status = Loc.TFormat("plugins.failed", row.Name, ex.Message); }
        await _ws.RefreshAsync();
    }

    public bool HasInstallStep(PluginItemViewModel item) => item.SelectedVersion is { } v
        && PluginInstallFlow.NeedsStep(new PluginInstallRequest(_ws.Client, item.Entry, v, item.Installed, item.InstalledVersion));

    public async Task SetEnabledAsync(PluginRowViewModel row, bool enabled)
    {
        try
        {
            if (enabled) _ws.Services.Core.Install.Plugins.Enable(_ws.Client.GameMiniDir, row.Item.Entry.Id);
            else _ws.Services.Core.Install.Plugins.Disable(_ws.Client.GameMiniDir, row.Item.Entry.Id);
            Status = Loc.TFormat(enabled ? "plugins.enabledNextLaunch" : "plugins.disabledNextLaunch", row.Name);
        }
        catch (Exception ex) { Status = Loc.TFormat("plugins.failed", row.Name, ex.Message); }   // fire-and-forget from the toggle
        await _ws.RefreshAsync();
    }

    public Task InstallAsync(PluginItemViewModel item) => InstallVersionAsync(RowOf(item), item.SelectedVersion?.Version);
    public Task EnableAsync(PluginItemViewModel item) => SetEnabledAsync(RowOf(item), true);
    private PluginRowViewModel RowOf(PluginItemViewModel item) => _all.First(r => ReferenceEquals(r.Item, item));

    [RelayCommand]
    private async Task CopySetFrom(ClientProfile source)
    {
        var src = _others.FirstOrDefault(c => c.Client.Id == source.Id);
        if (src is null) return;
        var plan = CopySetPlanner.Plan(src, new ClientColumn(_ws.Client, _ws.Inventory, _ws.Registry));
        var installs = plan.Where(i => i.Status == CopyStatus.Install).ToList();
        if (installs.Count == 0) { Status = Loc.TFormat("copy.nothing", source.Name); return; }
        var body = string.Join("\n", plan.Select(i => $"{i.Entry.Name}: {CopyStatusText(i)}"));
        if (!await _ws.Services.Confirm.AskAsync(Loc.TFormat("copy.title", source.Name, _ws.Client.Name), body, Loc.TFormat("copy.ok", installs.Count))) return;
        var ok = 0;
        foreach (var i in installs)
        {
            var v = i.Entry.Versions.First(x => x.Version == i.TargetVersion);
            try { await PluginDownloads.InstallAsync(_ws.Services.Core.Install, _ws.Client, i.Entry, v, null); ok++; }
            catch (Exception ex) { Status = Loc.TFormat("plugins.failed", i.Entry.Name, ex.Message); }
        }
        Status = Loc.TFormat("copy.done", source.Name, ok, plan.Count - installs.Count);
        await _ws.RefreshAsync();
    }

    private static string CopyStatusText(CopyItem i) => i.Status switch
    {
        CopyStatus.Install => Loc.TFormat("copy.status.install", i.TargetVersion),
        CopyStatus.AlreadyInstalled => Loc.T("copy.status.alreadyInstalled"),
        CopyStatus.SkippedDisabledOnSource => Loc.T("copy.status.disabledOnSource"),
        CopyStatus.NoCompatibleVersion => Loc.T("copy.status.noCompatible"),
        CopyStatus.NoFramework => Loc.T("copy.status.noFramework"),
        _ => i.Status.ToString(),
    };

    // ---- detail page + lightbox (same member names as the old PluginsViewModel so the XAML moves verbatim) ----
    partial void OnSelectedPluginChanged(PluginItemViewModel? value) => OnPropertyChanged(nameof(IsDetailOpen));
    partial void OnLightboxImageChanged(Bitmap? value) => OnPropertyChanged(nameof(IsLightboxOpen));
    [RelayCommand]
    private void OpenPlugin(PluginItemViewModel item)
    {
        SelectedPlugin = item;
        item.DependenciesLocked = _ws.Session.IsBusy;
        _ = item.RefreshDependenciesAsync();   // re-read every time the page opens: disk may have changed since (a launch, a remove)
        _ = item.EnsureDetailLoadedAsync(_ws.Services.Core.Install.Http, bmp => LightboxImage = bmp);
    }

    /// <summary>2.1.2 (discoverability): the plugin list row's extras pill opens the detail page already
    /// scrolled to DEPENDENCIES — same open as <see cref="OpenPlugin"/>, plus a one-shot signal the view
    /// uses to scroll once the section has actually rendered (awaits the refresh the open just started, so
    /// the section exists in the visual tree by the time the view goes looking for it).</summary>
    public event Action? ScrollToDependenciesRequested;
    [RelayCommand]
    private async Task OpenPluginDependencies(PluginItemViewModel item)
    {
        OpenPlugin(item);
        try { await item.DependencyRefresh; } catch { /* the view still scrolls to whatever is there */ }
        ScrollToDependenciesRequested?.Invoke();
    }

    [RelayCommand] private void CloseDetail() => SelectedPlugin = null;
    [RelayCommand] private void CloseLightbox() { var old = LightboxImage; LightboxImage = null; old?.Dispose(); }
    [RelayCommand] private void OpenLink(string? url) => Services.Browser.Open(url);
}

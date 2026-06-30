using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.ViewModels;

public enum PluginTab { Available, Installed, Updates }

public partial class PluginsViewModel : ObservableObject
{
    private readonly IPluginRegistryService _registry;
    private readonly IPluginInstaller _installer;
    private readonly IInstaller _frameworkInstaller;   // to read the installed framework version (compat)
    private readonly ISettingsStore _settings;
    private readonly HttpClient _http;

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string? _newRepoUrl;
    [ObservableProperty] private bool _hasPluginUpdates;
    [ObservableProperty] private PluginTab _activeTab = PluginTab.Available;
    [ObservableProperty] private int _installedCount;
    [ObservableProperty] private int _updateCount;

    // Full sorted list; Plugins is the filtered view of this.
    private readonly List<PluginItemViewModel> _allPlugins = new();

    public ObservableCollection<PluginItemViewModel> Plugins { get; } = new();
    public ObservableCollection<string> Repos { get; } = new();

    public bool IsTabAvailable => ActiveTab == PluginTab.Available;
    public bool IsTabInstalled => ActiveTab == PluginTab.Installed;
    public bool IsTabUpdates  => ActiveTab == PluginTab.Updates;

    public string InstalledTabLabel => InstalledCount > 0 ? $"Installed ({InstalledCount})" : "Installed";
    public string UpdatesTabLabel   => UpdateCount   > 0 ? $"Updates ({UpdateCount})"   : "Updates";

    public PluginsViewModel(IPluginRegistryService registry, IPluginInstaller installer,
        IInstaller frameworkInstaller, ISettingsStore settings, HttpClient http)
    {
        _registry = registry; _installer = installer; _frameworkInstaller = frameworkInstaller;
        _settings = settings; _http = http;
        _ = ReloadAsync();
    }

    partial void OnActiveTabChanged(PluginTab value)
    {
        OnPropertyChanged(nameof(IsTabAvailable));
        OnPropertyChanged(nameof(IsTabInstalled));
        OnPropertyChanged(nameof(IsTabUpdates));
        ApplyFilter();
    }

    [RelayCommand] private void SetTabAvailable() => ActiveTab = PluginTab.Available;
    [RelayCommand] private void SetTabInstalled() => ActiveTab = PluginTab.Installed;
    [RelayCommand] private void SetTabUpdates()   => ActiveTab = PluginTab.Updates;

    private void ApplyFilter()
    {
        Plugins.Clear();
        var source = ActiveTab switch
        {
            PluginTab.Installed => _allPlugins.Where(p => p.Installed),
            PluginTab.Updates   => _allPlugins.Where(p => p.HasUpdate),
            _                   => (IEnumerable<PluginItemViewModel>)_allPlugins
        };
        foreach (var p in source) Plugins.Add(p);
    }

    private void RefreshCounts()
    {
        InstalledCount   = _allPlugins.Count(p => p.Installed);
        UpdateCount      = _allPlugins.Count(p => p.HasUpdate);
        HasPluginUpdates = UpdateCount > 0;
        OnPropertyChanged(nameof(InstalledTabLabel));
        OnPropertyChanged(nameof(UpdatesTabLabel));
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        var cfg = _settings.Load();
        var gameMini = cfg.GameMiniDir;

        // Always include the stable registry so stable updates are never hidden from testing-channel users.
        // On testing channel: stable first, then testing on top (testing overrides same plugin ID).
        var urls = new List<Uri>();
        if (ChannelManifests.IsTesting(cfg.Channel))
            urls.Add(ChannelManifests.PluginRegistry(null));
        urls.Add(ChannelManifests.PluginRegistry(cfg.Channel));
        Repos.Clear();
        foreach (var repo in cfg.ExtraPluginRepos)
        {
            Repos.Add(repo);
            if (Uri.TryCreate(repo, UriKind.Absolute, out var u)) urls.Add(u);
        }

        var framework = gameMini is null ? null : _frameworkInstaller.ReadInstalledVersion(gameMini);

        try
        {
            var entries = await _registry.FetchAllAsync(urls);
            _allPlugins.Clear();
            foreach (var e in entries)
            {
                try
                {
                    // Installed if the plugin's DLL is present anywhere under stellar/plugins (adopts
                    // old-launcher installs); the version is known only when our marker is present.
                    var dll = CanonicalDll(e);
                    var installed = gameMini is not null && dll is not null && _installer.FindInstalledDll(gameMini, dll) is not null;
                    var version = gameMini is null ? null : _installer.InstalledVersion(gameMini, e.Id);
                    _allPlugins.Add(new PluginItemViewModel(e, installed, version, framework, this));
                }
                catch { /* skip a malformed entry rather than failing the whole list */ }
            }
            _allPlugins.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
            RefreshCounts();
            ApplyFilter();
            Status = _allPlugins.Count == 0 ? "No plugins found." : $"{_allPlugins.Count} plugins available.";
        }
        catch (Exception ex) { Status = $"failed to load plugins: {ex.Message}"; }
    }

    public async Task InstallAsync(PluginItemViewModel item)
    {
        var gameMini = _settings.Load().GameMiniDir;
        if (gameMini is null) { Status = "set the game path in Settings first."; return; }
        var v = item.SelectedVersion;
        if (v is null) { item.Action = "no version selected"; return; }
        try
        {
            item.Action = "downloading…";
            using var buffer = new MemoryStream();
            long lastTick = -1;
            var progress = new Progress<DownloadProgress>(p =>
            {
                long tick = p.Fraction is { } f ? (long)(f * 100) : p.BytesRead >> 20;
                if (tick != lastTick) { lastTick = tick; item.Action = DownloadStatus.Line("downloading…", p); }
            });
            await _http.DownloadToAsync(new Uri(v.DllUrl), buffer, progress);
            buffer.Position = 0;
            // Install under the canonical assembly filename (not the version-suffixed bucket name),
            // so it matches/overwrites any existing copy and the framework loads exactly one.
            var fileName = v.Dll ?? Path.GetFileName(new Uri(v.DllUrl).LocalPath);
            await _installer.InstallAsync(buffer, v.Sha256, gameMini, item.Entry.Id, fileName, v.Version);
            item.MarkInstalled(v.Version);
            item.Action = $"installed v{v.Version}";
            RefreshCounts();
            ApplyFilter();
        }
        catch (Exception ex) { item.Action = $"failed: {ex.Message}"; }
    }

    public Task RemoveAsync(PluginItemViewModel item)
    {
        var gameMini = _settings.Load().GameMiniDir;
        if (gameMini is null) { Status = "set the game path in Settings first."; return Task.CompletedTask; }
        try
        {
            _installer.Remove(gameMini, item.Entry.Id, item.CanonicalDll);
            item.MarkRemoved();
            item.Action = "removed";
            RefreshCounts();
            ApplyFilter();
        }
        catch (Exception ex) { item.Action = $"failed: {ex.Message}"; }
        return Task.CompletedTask;
    }

    // Canonical DLL filename for a plugin (from its newest version), for install/detect/remove.
    private static string? CanonicalDll(StellarLauncher.Core.Model.PluginEntry e)
    {
        if (e.Versions is null || e.Versions.Count == 0) return null;
        var v = e.Versions[0];
        if (!string.IsNullOrEmpty(v.Dll)) return v.Dll;
        return Uri.TryCreate(v.DllUrl, UriKind.Absolute, out var u) ? Path.GetFileName(u.LocalPath) : null;
    }

    [RelayCommand]
    private async Task AddRepo()
    {
        if (string.IsNullOrWhiteSpace(NewRepoUrl) ||
            !Uri.TryCreate(NewRepoUrl, UriKind.Absolute, out _)) { Status = "enter a valid repo URL."; return; }
        var cfg = _settings.Load();
        if (!cfg.ExtraPluginRepos.Contains(NewRepoUrl)) { cfg.ExtraPluginRepos.Add(NewRepoUrl); _settings.Save(cfg); }
        NewRepoUrl = "";
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task RemoveRepo(string url)
    {
        var cfg = _settings.Load();
        if (cfg.ExtraPluginRepos.Remove(url)) _settings.Save(cfg);
        await ReloadAsync();
    }
}

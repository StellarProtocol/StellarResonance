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

public partial class PluginsViewModel : ObservableObject
{
    private static readonly Uri CuratedRegistry = new("https://minio.revette.io/stellar/plugins.json");

    private readonly IPluginRegistryService _registry;
    private readonly IPluginInstaller _installer;
    private readonly IInstaller _frameworkInstaller;   // to read the installed framework version (compat)
    private readonly ISettingsStore _settings;
    private readonly HttpClient _http;

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string? _newRepoUrl;

    public ObservableCollection<PluginItemViewModel> Plugins { get; } = new();
    public ObservableCollection<string> Repos { get; } = new();

    public PluginsViewModel(IPluginRegistryService registry, IPluginInstaller installer,
        IInstaller frameworkInstaller, ISettingsStore settings, HttpClient http)
    {
        _registry = registry; _installer = installer; _frameworkInstaller = frameworkInstaller;
        _settings = settings; _http = http;
        _ = ReloadAsync();
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        var cfg = _settings.Load();
        var gameMini = cfg.GameMiniDir;

        var urls = new List<Uri> { CuratedRegistry };
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
            Plugins.Clear();
            foreach (var e in entries.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                // Installed if the plugin's DLL is present anywhere under stellar/plugins (adopts
                // old-launcher installs); the version is known only when our marker is present.
                var dll = CanonicalDll(e);
                var installed = gameMini is not null && dll is not null && _installer.FindInstalledDll(gameMini, dll) is not null;
                var version = gameMini is null ? null : _installer.InstalledVersion(gameMini, e.Id);
                Plugins.Add(new PluginItemViewModel(e, installed, version, framework, this));
            }
            Status = Plugins.Count == 0 ? "No plugins found." : $"{Plugins.Count} plugins available.";
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
        }
        catch (Exception ex) { item.Action = $"failed: {ex.Message}"; }
    }

    public Task RemoveAsync(PluginItemViewModel item)
    {
        var gameMini = _settings.Load().GameMiniDir;
        if (gameMini is null) { Status = "set the game path in Settings first."; return Task.CompletedTask; }
        try { _installer.Remove(gameMini, item.Entry.Id, item.CanonicalDll); item.MarkRemoved(); item.Action = "removed"; }
        catch (Exception ex) { item.Action = $"failed: {ex.Message}"; }
        return Task.CompletedTask;
    }

    // Canonical DLL filename for a plugin (from its newest version), for install/detect/remove.
    private static string? CanonicalDll(StellarLauncher.Core.Model.PluginEntry e)
    {
        if (e.Versions.Count == 0) return null;
        var v = e.Versions[0];
        return v.Dll ?? Path.GetFileName(new Uri(v.DllUrl).LocalPath);
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

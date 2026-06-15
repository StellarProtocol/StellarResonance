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
    private readonly ISettingsStore _settings;
    private readonly HttpClient _http;

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string? _newRepoUrl;

    public ObservableCollection<PluginItemViewModel> Plugins { get; } = new();
    public ObservableCollection<string> Repos { get; } = new();

    public PluginsViewModel(IPluginRegistryService registry, IPluginInstaller installer,
        ISettingsStore settings, HttpClient http)
    {
        _registry = registry; _installer = installer; _settings = settings; _http = http;
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

        try
        {
            var entries = await _registry.FetchAllAsync(urls);
            Plugins.Clear();
            foreach (var e in entries.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var installed = gameMini is not null && _installer.IsInstalled(gameMini, e.Id);
                Plugins.Add(new PluginItemViewModel(e, installed, this));
            }
            Status = Plugins.Count == 0 ? "No plugins found." : $"{Plugins.Count} plugins available.";
        }
        catch (Exception ex) { Status = $"failed to load plugins: {ex.Message}"; }
    }

    public async Task InstallAsync(PluginItemViewModel item)
    {
        var gameMini = _settings.Load().GameMiniDir;
        if (gameMini is null) { Status = "set the game path in Settings first."; return; }
        try
        {
            item.Action = "downloading…";
            using var stream = await _http.GetStreamAsync(item.Entry.DllUrl);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            buffer.Position = 0;
            var fileName = Path.GetFileName(new Uri(item.Entry.DllUrl).LocalPath);
            await _installer.InstallAsync(buffer, item.Entry.Sha256, gameMini,
                item.Entry.Id, fileName, item.Entry.Version);
            item.Installed = true;
            item.Action = $"installed v{item.Entry.Version}";
        }
        catch (Exception ex) { item.Action = $"failed: {ex.Message}"; }
    }

    public Task RemoveAsync(PluginItemViewModel item)
    {
        var gameMini = _settings.Load().GameMiniDir;
        if (gameMini is null) { Status = "set the game path in Settings first."; return Task.CompletedTask; }
        try { _installer.Remove(gameMini, item.Entry.Id); item.Installed = false; item.Action = "removed"; }
        catch (Exception ex) { item.Action = $"failed: {ex.Message}"; }
        return Task.CompletedTask;
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

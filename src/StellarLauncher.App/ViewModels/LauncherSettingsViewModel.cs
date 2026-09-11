using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels.Shell;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.ViewModels;

// A parameter-object bundle (records are exempt from the ≤ 6 ctor-deps guardrail, which targets classes).
public sealed record LauncherServices(ILauncherUpdateService Updates, ILauncherSelfUpdater SelfUpdater, IPlatformInfo Platform,
    IConfigStore Store, HttpClient Http, IFileSystem Fs);

/// <summary>Only what belongs to the launcher itself (mockup #launcher, spec § 5.8).</summary>
public sealed partial class LauncherSettingsViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private readonly LauncherServices _svc;
    private LauncherManifest? _remote;
    private bool _loading;

    public ObservableCollection<string> Sources { get; } = new();
    [ObservableProperty] private bool _testingChannel, _keepOpen, _startOnLastClient, _showMatrix, _isDownloading;
    [ObservableProperty] private string? _newSource;
    [ObservableProperty] private string _status = "", _availableLabel = "not checked yet";
    [ObservableProperty] private double _downloadPercent;

    public string InstalledLabel => $"v{AppInfo.LauncherVersion}";
    public string SettingsPath => _svc.Store.SettingsPath;
    public string FormatLine
    {
        get
        {
            var n = _shell.Config.Clients.Count;
            var backup = _svc.Fs.File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SettingsPath)!, "settings.v1.json")) ? "settings.v1.json backup present" : "no v1 backup";
            return $"v2 · {n} client{(n == 1 ? "" : "s")} · {backup}";
        }
    }

    public LauncherSettingsViewModel(ShellViewModel shell, LauncherServices svc)
    {
        _shell = shell; _svc = svc;
        _loading = true;
        var l = shell.Config.Launcher;
        TestingChannel = ChannelManifests.IsTesting(l.Channel); KeepOpen = l.KeepOpen; StartOnLastClient = l.StartOn == "lastClient"; ShowMatrix = l.ShowMatrix;
        foreach (var s in l.PluginSources) Sources.Add(s);
        _loading = false;
    }

    private void Persist() { if (!_loading) _shell.SaveConfig(); }
    partial void OnTestingChannelChanged(bool value) { if (_loading) return; _shell.Config.Launcher.Channel = value ? "testing" : "stable"; Persist(); }
    partial void OnKeepOpenChanged(bool value) { if (_loading) return; _shell.Config.Launcher.KeepOpen = value; Persist(); }
    partial void OnStartOnLastClientChanged(bool value) { if (_loading) return; _shell.Config.Launcher.StartOn = value ? "lastClient" : "dashboard"; Persist(); }
    partial void OnShowMatrixChanged(bool value) { if (_loading) return; _shell.Config.Launcher.ShowMatrix = value; Persist(); }

    [RelayCommand]
    private void AddSource()
    {
        if (!Uri.TryCreate(NewSource, UriKind.Absolute, out _)) { Status = "enter a valid registry URL"; return; }
        if (!Sources.Contains(NewSource!)) { Sources.Add(NewSource!); _shell.Config.Launcher.PluginSources.Add(NewSource!); Persist(); }
        NewSource = ""; Status = "";
    }

    [RelayCommand]
    private void RemoveSource(string url)
    {
        Sources.Remove(url); _shell.Config.Launcher.PluginSources.Remove(url); Persist();
    }

    // ---- launcher self-update (moved from HomeViewModel) ----
    /// <summary>Sets (or clears) the rail's update banner. Also run once at startup by the composition root.</summary>
    public static async Task CheckUpdatesAsync(ShellViewModel shell, LauncherServices svc)
    {
        try
        {
            var channel = shell.Config.Launcher.Channel;
            var m = await svc.Updates.FetchAsync(ChannelManifests.LauncherManifest(channel));
            if (ChannelManifests.IsTesting(channel))
            {
                try { var stable = await svc.Updates.FetchAsync(ChannelManifests.LauncherManifest(null)); if (VersionService.IsNewer(stable.Version, m.Version)) m = stable; }
                catch (Exception) { /* stable manifest unavailable — the testing one stands */ }
            }
            shell.LauncherUpdateAvailable = VersionService.IsNewer(m.Version, AppInfo.LauncherVersion);
            shell.LauncherUpdateText = shell.LauncherUpdateAvailable ? $"↑ Launcher v{m.Version} available" : "";
        }
        catch (Exception) { /* offline — no banner */ }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        await CheckUpdatesAsync(_shell, _svc);
        try { _remote = await _svc.Updates.FetchAsync(ChannelManifests.LauncherManifest(_shell.Config.Launcher.Channel)); AvailableLabel = $"v{_remote.Version} · {_remote.Date}"; }
        catch (Exception ex) { AvailableLabel = $"offline — {ex.Message}"; }
    }

    [RelayCommand]
    private async Task UpdateLauncherAsync()
    {
        if (_remote is null) { await CheckUpdatesAsync(); if (_remote is null) return; }
        try
        {
            var sha = _remote.ShaFor(_svc.Platform.IsWindows);
            if (string.IsNullOrEmpty(sha)) { Status = "update unavailable (no checksum)"; return; }
            using var buffered = new MemoryStream();
            long lastTick = -1;
            var progress = new Progress<DownloadProgress>(p =>
            {
                if (p.Fraction is { } f) DownloadPercent = f * 100;
                long tick = p.Fraction is { } g ? (long)(g * 100) : p.BytesRead >> 20;
                if (tick != lastTick) { lastTick = tick; Status = DownloadStatus.Line("downloading launcher…", p); }
            });
            IsDownloading = true;
            try { await _svc.Http.DownloadToAsync(new Uri(_remote.DownloadUrlFor(_svc.Platform.IsWindows)), buffered, progress); }
            finally { IsDownloading = false; }
            buffered.Position = 0;
            var staging = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stellar-launcher-update");
            await _svc.SelfUpdater.StageAsync(buffered, sha, staging);
            var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("cannot resolve launcher path");
            Status = "applying update — restarting…";
            _svc.SelfUpdater.ApplyAndRestart(staging, System.IO.Path.GetDirectoryName(exePath)!, System.IO.Path.GetFileName(exePath), _svc.Platform.IsWindows);
        }
        catch (Exception ex) { Status = $"update failed: {ex.Message}"; }
    }

    // ---- data ----
    public void Export(string path)
    {
        try
        {
            _svc.Fs.File.WriteAllText(path, JsonSerializer.Serialize(_shell.Config.Clients, ConfigStore.Json));
            Status = $"exported {_shell.Config.Clients.Count} client(s) to {path}";
        }
        catch (Exception ex) { Status = $"export failed: {ex.Message}"; }
    }

    /// <summary>Import a clients list: folders re-validated, names uniqued, ids and accents reassigned.</summary>
    public void Import(string path)
    {
        List<ClientProfile>? imported;
        try { imported = JsonSerializer.Deserialize<List<ClientProfile>>(_svc.Fs.File.ReadAllText(path), ConfigStore.Json); }
        catch (Exception ex) { Status = $"import failed: {ex.Message}"; return; }
        if (imported is null) { Status = "import failed: not a clients file"; return; }
        var cfg = _shell.Config; var added = 0; var skipped = 0;
        foreach (var c in imported)
        {
            if (string.IsNullOrWhiteSpace(c.GameMiniDir) || !_svc.Fs.Directory.Exists(c.GameMiniDir)
                || cfg.Clients.Any(x => ClientCandidates.SamePath(x.GameMiniDir, c.GameMiniDir, _svc.Platform.IsWindows))) { skipped++; continue; }
            c.Id = ClientIds.New(); c.Accent = AccentPalette.Next(cfg.Clients.Select(x => x.Accent));
            var proposed = string.IsNullOrWhiteSpace(c.Name) ? ClientNaming.ProposeName(c.GameMiniDir) : c.Name.Trim();
            var name = proposed;
            for (var n = 2; cfg.NameTaken(name); n++) name = $"{proposed} {n}";
            c.Name = name; cfg.Clients.Add(c); added++;
        }
        _shell.SaveConfig(); _shell.Reload();
        Status = $"imported {added}, skipped {skipped} (missing folder or already a client)";
        OnPropertyChanged(nameof(FormatLine));
    }
}

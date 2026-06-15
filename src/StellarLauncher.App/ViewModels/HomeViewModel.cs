using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    public const string LauncherVersion = "1.0.0";

    private readonly ISettingsStore _settings;
    private readonly IGameLocator _locator;
    private readonly IDoorstopToggle _doorstop;
    private readonly IInstaller _installer;
    private readonly IGameLauncher _launcher;
    private readonly IVersionService _version;
    private readonly IPlatformInfo _platform;
    private readonly ILauncherUpdateService _launcherUpdates;
    private readonly IGameDetector _detector;
    private readonly ILauncherSelfUpdater _selfUpdater;

    [ObservableProperty] private string _gameStatus = "Detecting…";
    [ObservableProperty] private string _frameworkStatus = "—";
    [ObservableProperty] private string _statusLine = "";
    [ObservableProperty] private bool _modded = true;
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private bool _launcherUpdateAvailable;
    [ObservableProperty] private string _launcherUpdateText = "";
    public ObservableCollection<VersionManifest> Changelog { get; } = new();

    private LauncherSettings _cfg = new();
    private VersionManifest? _remote;
    private LauncherManifest? _remoteLauncher;

    public HomeViewModel(ISettingsStore settings, IGameLocator locator, IDoorstopToggle doorstop,
        IInstaller installer, IGameLauncher launcher, IVersionService version, IPlatformInfo platform,
        ILauncherUpdateService launcherUpdates, IGameDetector detector, ILauncherSelfUpdater selfUpdater)
    {
        _settings = settings; _locator = locator; _doorstop = doorstop;
        _installer = installer; _launcher = launcher; _version = version; _platform = platform;
        _launcherUpdates = launcherUpdates; _detector = detector;
        _selfUpdater = selfUpdater;
        _ = RefreshAsync();
        _ = CheckLauncherUpdateAsync();
    }

    private async Task CheckLauncherUpdateAsync()
    {
        try
        {
            var channel = _settings.Load().Channel;
            var m = await _launcherUpdates.FetchAsync(ChannelManifests.LauncherManifest(channel));
            _remoteLauncher = m;
            if (VersionService.IsNewer(m.Version, LauncherVersion))
            {
                LauncherUpdateText = $"Launcher v{m.Version} available";
                LauncherUpdateAvailable = true;
            }
        }
        catch { /* offline or not yet published — no banner */ }
    }

    [RelayCommand]
    private async Task UpdateLauncherAsync()
    {
        if (_remoteLauncher is null) return;
        try
        {
            var url = _remoteLauncher.DownloadUrlFor(_platform.IsWindows);
            var sha = _remoteLauncher.ShaFor(_platform.IsWindows);
            if (string.IsNullOrEmpty(sha)) { LauncherUpdateText = "update unavailable (no checksum)"; return; }

            LauncherUpdateText = "downloading launcher…";
            using var http = new System.Net.Http.HttpClient();
            using var zip = await http.GetStreamAsync(url);
            using var buffered = new MemoryStream();
            await zip.CopyToAsync(buffered);
            buffered.Position = 0;

            var staging = Path.Combine(Path.GetTempPath(), "stellar-launcher-update");
            await _selfUpdater.StageAsync(buffered, sha, staging);

            var exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("cannot resolve launcher path");
            var installDir = Path.GetDirectoryName(exePath)!;
            var exeName = Path.GetFileName(exePath);

            LauncherUpdateText = "applying update — restarting…";
            _selfUpdater.ApplyAndRestart(staging, installDir, exeName, _platform.IsWindows); // exits the process
        }
        catch (Exception ex) { LauncherUpdateText = $"update failed: {ex.Message}"; }
    }

    private string? GameMini => _cfg.GameMiniDir;
    private string? DoorstopPath => GameMini is null ? null : Path.Combine(GameMini, "doorstop_config.ini");

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _cfg = _settings.Load();
        Modded = _cfg.Modded;

        GameStatus = GameMini is { } g && Directory.Exists(g) ? g : "Not found — set it in Settings";

        var installed = GameMini is null ? null : _installer.ReadInstalledVersion(GameMini);
        FrameworkStatus = installed is null ? "Not installed" : $"v{installed} installed";

        try
        {
            _remote = await _version.FetchAsync(ChannelManifests.FrameworkVersion(_cfg.Channel));
            Changelog.Clear();
            Changelog.Add(_remote);
            UpdateAvailable = installed is null || VersionService.IsNewer(_remote.Version, installed);
        }
        catch (Exception ex) { StatusLine = $"offline — {ex.Message}"; }
    }

    [RelayCommand]
    private async Task InstallOrUpdateAsync()
    {
        if (GameMini is null || _remote is null) { StatusLine = "set the game path first"; return; }
        if (!VersionService.LauncherSupported(_remote.MinLauncherVersion, LauncherVersion))
        { StatusLine = "update the launcher first"; return; }

        try
        {
            StatusLine = "downloading…";
            using var http = new System.Net.Http.HttpClient();
            using var zip = await http.GetStreamAsync(_remote.BundleUrl);
            using var buffered = new MemoryStream();
            await zip.CopyToAsync(buffered);
            buffered.Position = 0;
            await _installer.InstallAsync(buffered, _remote.Sha256, GameMini, _remote.Version);
            StatusLine = $"installed v{_remote.Version}";
            await RefreshAsync();
        }
        catch (Exception ex) { StatusLine = $"install failed: {ex.Message}"; }
    }

    partial void OnModdedChanged(bool value)
    {
        // Persist the preference regardless of install state…
        _cfg.Modded = value;
        _settings.Save(_cfg);
        // …but only flip the doorstop file once it actually exists (post-install).
        if (DoorstopPath is { } p && File.Exists(p))
        {
            _doorstop.SetEnabled(p, value);
            StatusLine = value ? "Modded" : "Vanilla";
        }
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (GameMini is null) { StatusLine = "set the game path first"; return; }
        try
        {
            var exe = StarLauncherExe(GameMini);
            var request = new LaunchRequest(exe, _cfg.Runner, _cfg.WinePrefix, _detector.DetectUmu());
            StatusLine = "launching…";
            var proc = _launcher.Launch(request);
            if (proc is null) { StatusLine = "launch failed: process did not start"; return; }

            // Give the runner a moment; if it dies fast, surface the failure instead of a frozen "launching…".
            await Task.Delay(2500);
            StatusLine = proc.HasExited && proc.ExitCode != 0
                ? $"launch failed (runner exited {proc.ExitCode}) — check Runner / WINEPREFIX in Settings"
                : "game launching…";
        }
        catch (Exception ex) { StatusLine = $"launch failed: {ex.Message}"; }
    }

    // game_mini → release_x → game → StarLauncher : StarLauncher.exe is three parents up.
    private static string StarLauncherExe(string gameMini)
    {
        var starLauncherDir = Directory.GetParent(gameMini)!.Parent!.Parent!.FullName;
        return Path.Combine(starLauncherDir, "StarLauncher.exe");
    }
}

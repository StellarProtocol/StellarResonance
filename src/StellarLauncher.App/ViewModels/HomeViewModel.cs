using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    // Stamped at publish time via -p:Version (see release.yml); the dispatch input is the
    // single source of truth. Strips the "+gitsha" build-metadata suffix. Local dev builds
    // report the csproj default (0.0.0-dev) — correct, since a local build isn't a release.
    public static readonly string LauncherVersion =
        typeof(HomeViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+', 2)[0]
        ?? "0.0.0";

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
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadPercent;   // 0..100; bound to the ProgressBar
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

            using var http = new System.Net.Http.HttpClient();
            using var buffered = new MemoryStream();
            var progress = MakeProgress("downloading launcher…", t => LauncherUpdateText = t);
            try { await http.DownloadToAsync(new Uri(url), buffered, progress); }
            finally { IsDownloading = false; }
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

    // Builds a progress sink that drives the ProgressBar and a throttled status line (updated only when
    // the whole-percent — or, for unknown-length downloads, the whole-MB — ticks over, to avoid UI churn).
    private IProgress<DownloadProgress> MakeProgress(string verb, Action<string> setText)
    {
        IsDownloading = true;
        DownloadPercent = 0;
        long lastTick = -1;
        return new Progress<DownloadProgress>(p =>
        {
            if (p.Fraction is { } f) DownloadPercent = f * 100;
            long tick = p.Fraction is { } g ? (long)(g * 100) : p.BytesRead >> 20;
            if (tick != lastTick) { lastTick = tick; setText(DownloadStatus.Line(verb, p)); }
        });
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
            using var http = new System.Net.Http.HttpClient();
            using var buffered = new MemoryStream();
            var progress = MakeProgress("downloading…", t => StatusLine = t);
            try { await http.DownloadToAsync(new Uri(_remote.BundleUrl), buffered, progress); }
            finally { IsDownloading = false; }
            buffered.Position = 0;
            StatusLine = "installing…";
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

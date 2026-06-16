using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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

    /// <summary>Display label for the launcher's own version (e.g. "Launcher v1.2.4").</summary>
    public string LauncherVersionLabel => $"Launcher v{LauncherVersion}";

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
    private readonly IDxvkNvapiInstaller _dxvkNvapi;
    private readonly IBepInExConfig _bepinex;

    [ObservableProperty] private string _gameStatus = "Detecting…";
    [ObservableProperty] private string _frameworkStatus = "—";
    [ObservableProperty] private string _statusLine = "";
    [ObservableProperty] private bool _modded = true;
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private bool _launcherUpdateAvailable;
    [ObservableProperty] private string _launcherUpdateText = "";
    [ObservableProperty] private bool _isLauncherDownloading;   // drives the banner progress bar
    [ObservableProperty] private bool _isFrameworkDownloading;  // drives the footer progress bar
    [ObservableProperty] private double _downloadPercent;       // 0..100; shared (one download at a time)

    // Framework version picker.
    [ObservableProperty] private VersionManifest? _selectedVersion;   // bound to the ComboBox
    [ObservableProperty] private bool _hasVersions;
    [ObservableProperty] private string _installAction = "Install";   // button label, reflects the selection
    [ObservableProperty] private bool _isDowngrade;
    [ObservableProperty] private bool _canChangeFramework;            // selection installable (launcher supported)
    [ObservableProperty] private bool _confirmVisible;                // inline "review then confirm" step
    public ObservableCollection<VersionManifest> Versions { get; } = new();   // all releases, newest first
    public ObservableCollection<VersionManifest> Changelog { get; } = new();  // the SELECTED version's changelog

    private LauncherSettings _cfg = new();
    private FrameworkManifest? _manifest;
    private string? _installedFramework;
    private LauncherManifest? _remoteLauncher;

    public HomeViewModel(ISettingsStore settings, IGameLocator locator, IDoorstopToggle doorstop,
        IInstaller installer, IGameLauncher launcher, IVersionService version, IPlatformInfo platform,
        ILauncherUpdateService launcherUpdates, IGameDetector detector, ILauncherSelfUpdater selfUpdater,
        IDxvkNvapiInstaller dxvkNvapi, IBepInExConfig bepinex)
    {
        _settings = settings; _locator = locator; _doorstop = doorstop;
        _installer = installer; _launcher = launcher; _version = version; _platform = platform;
        _launcherUpdates = launcherUpdates; _detector = detector;
        _selfUpdater = selfUpdater; _dxvkNvapi = dxvkNvapi; _bepinex = bepinex;
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
            IsLauncherDownloading = true;
            try { await http.DownloadToAsync(new Uri(url), buffered, progress); }
            finally { IsLauncherDownloading = false; }
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

        _installedFramework = GameMini is null ? null : _installer.ReadInstalledVersion(GameMini);
        FrameworkStatus = _installedFramework is null ? "Not installed" : $"v{_installedFramework} installed";

        try
        {
            _manifest = await _version.FetchAsync(ChannelManifests.FrameworkVersion(_cfg.Channel));
            Versions.Clear();
            foreach (var v in _manifest.Versions) Versions.Add(v);
            HasVersions = Versions.Count > 0;
            // Default to the channel's latest; this fires OnSelectedVersionChanged to refresh the panel.
            SelectedVersion = Versions.FirstOrDefault(v => v.Version == _manifest.Latest) ?? Versions.FirstOrDefault();
            UpdateAvailable = HasVersions &&
                (_installedFramework is null || VersionService.IsNewer(_manifest.Latest, _installedFramework));
        }
        catch (Exception ex) { StatusLine = $"offline — {ex.Message}"; }
    }

    // Keep the changelog panel, button label, and downgrade/compat state in sync with the dropdown.
    partial void OnSelectedVersionChanged(VersionManifest? value)
    {
        ConfirmVisible = false;
        Changelog.Clear();
        if (value is null) { CanChangeFramework = false; InstallAction = "Install"; IsDowngrade = false; return; }
        Changelog.Add(value);

        CanChangeFramework = VersionService.LauncherSupported(value.MinLauncherVersion, LauncherVersion);
        if (!CanChangeFramework) { InstallAction = "Update launcher first"; IsDowngrade = false; return; }

        if (_installedFramework is null)
        {
            InstallAction = $"Install v{value.Version}"; IsDowngrade = false;
        }
        else if (VersionService.IsNewer(value.Version, _installedFramework))
        {
            InstallAction = $"Update to v{value.Version}"; IsDowngrade = false;
        }
        else if (VersionService.IsNewer(_installedFramework, value.Version))
        {
            InstallAction = $"Downgrade to v{value.Version}"; IsDowngrade = true;
        }
        else
        {
            InstallAction = $"Reinstall v{value.Version}"; IsDowngrade = false;
        }
    }

    // Step 1: reveal the inline confirm bar (the changelog above is the "review").
    [RelayCommand]
    private void RequestFrameworkChange() { if (CanChangeFramework) ConfirmVisible = true; }

    [RelayCommand]
    private void CancelFrameworkChange() => ConfirmVisible = false;

    // Step 2: actually download + install the selected version.
    [RelayCommand]
    private async Task ConfirmFrameworkChangeAsync()
    {
        ConfirmVisible = false;
        if (GameMini is null || SelectedVersion is null) { StatusLine = "set the game path first"; return; }
        var target = SelectedVersion;
        if (!VersionService.LauncherSupported(target.MinLauncherVersion, LauncherVersion))
        { StatusLine = "update the launcher first"; return; }

        try
        {
            using var http = new System.Net.Http.HttpClient();
            using var buffered = new MemoryStream();
            var progress = MakeProgress($"downloading v{target.Version}…", t => StatusLine = t);
            IsFrameworkDownloading = true;
            try { await http.DownloadToAsync(new Uri(target.BundleUrl), buffered, progress); }
            finally { IsFrameworkDownloading = false; }
            buffered.Position = 0;
            StatusLine = $"installing v{target.Version}…";
            await _installer.InstallAsync(buffered, target.Sha256, GameMini, target.Version);
            StatusLine = $"installed v{target.Version}";
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
            var cfg = _settings.Load();   // pick up the latest Settings (esync/fsync/overlay/dxvk-nvapi)

            // Tune the game's BepInEx logging for prod (fast) vs debug (console + crash-flush).
            _bepinex.ApplyMode(GameMini, cfg.DebugLogging);

            // Linux: ensure DXVK-NVAPI is in the prefix before launch (best-effort).
            if (!_platform.IsWindows && cfg.DxvkNvapi && !string.IsNullOrWhiteSpace(cfg.WinePrefix))
            {
                try { StatusLine = "checking DXVK-NVAPI…"; StatusLine = await _dxvkNvapi.EnsureAsync(cfg.WinePrefix!); }
                catch (Exception ex) { StatusLine = $"DXVK-NVAPI skipped: {ex.Message}"; }
            }

            var exe = StarLauncherExe(GameMini);
            var request = new LaunchRequest(exe, cfg.Runner, cfg.WinePrefix, _detector.DetectUmu(),
                Esync: cfg.Esync, Fsync: cfg.Fsync, FpsOverlay: cfg.FpsOverlay, DxvkNvapi: cfg.DxvkNvapi,
                StellarPerf: cfg.StellarPerf);
            StatusLine = "launching…";
            var proc = _launcher.Launch(request);
            if (proc is null) { StatusLine = "launch failed: process did not start"; return; }

            // Give the runner a moment; if it dies fast, surface the failure instead of a frozen "launching…".
            await Task.Delay(2500);
            StatusLine = proc.HasExited && proc.ExitCode != 0
                ? $"launch failed (runner exited {proc.ExitCode}) — check Runner / WINEPREFIX in Settings"
                : "game running";
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

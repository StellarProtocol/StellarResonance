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
    private readonly IInteropWatch _interop;

    [ObservableProperty] private string _gameStatus = "Detecting…";
    [ObservableProperty] private bool _needsGameSetup;   // true when no game is set — drives onboarding
    [ObservableProperty] private string _frameworkStatus = "—";
    [ObservableProperty] private string _statusLine = "";
    [ObservableProperty] private bool _modded = true;
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private bool _launcherUpdateAvailable;
    [ObservableProperty] private string _launcherUpdateText = "";
    [ObservableProperty] private bool _isLauncherDownloading;   // drives the banner progress bar
    [ObservableProperty] private bool _isFrameworkDownloading;  // drives the footer progress bar
    [ObservableProperty] private bool _isPreparingLaunch;       // first-run/post-update interop generation in progress
    [ObservableProperty] private bool _launchProgressIndeterminate;  // slow in-memory gen phase: animate (no count yet)
    [ObservableProperty] private double _downloadPercent;       // 0..100; shared (one download/gen at a time)

    /// <summary>Footer progress bar shows during a framework download OR interop (re)generation.</summary>
    public bool FooterProgressVisible => IsFrameworkDownloading || IsPreparingLaunch;
    partial void OnIsFrameworkDownloadingChanged(bool value) => OnPropertyChanged(nameof(FooterProgressVisible));
    partial void OnIsPreparingLaunchChanged(bool value) => OnPropertyChanged(nameof(FooterProgressVisible));

    // Framework version picker.
    [ObservableProperty] private VersionManifest? _selectedVersion;   // bound to the ComboBox
    [ObservableProperty] private bool _hasVersions;
    [ObservableProperty] private string _installAction = "Install";   // button label, reflects the selection
    [ObservableProperty] private bool _isDowngrade;
    [ObservableProperty] private bool _canChangeFramework;            // selection installable (launcher supported)
    [ObservableProperty] private bool _isFrameworkInstall;
    [ObservableProperty] private bool _isFrameworkUpdate;
    [ObservableProperty] private bool _isFrameworkReinstall = true;
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
        IDxvkNvapiInstaller dxvkNvapi, IBepInExConfig bepinex, IInteropWatch interop)
    {
        _settings = settings; _locator = locator; _doorstop = doorstop;
        _installer = installer; _launcher = launcher; _version = version; _platform = platform;
        _launcherUpdates = launcherUpdates; _detector = detector;
        _selfUpdater = selfUpdater; _dxvkNvapi = dxvkNvapi; _bepinex = bepinex; _interop = interop;
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

        // Auto-detect on first run (or if the saved path no longer exists) and persist it, so the
        // game is usable straight away — no manual trip through Settings → Save required.
        if (string.IsNullOrWhiteSpace(_cfg.GameMiniDir) || !Directory.Exists(_cfg.GameMiniDir))
        {
            var found = _detector.Detect();
            if (found.Count > 0)
            {
                _cfg.GameMiniDir = found[0];
                _settings.Save(_cfg);
            }
        }

        Modded = _cfg.Modded;

        var haveGame = GameMini is { } g && Directory.Exists(g);
        NeedsGameSetup = !haveGame;
        GameStatus = haveGame ? GameMini! : "Not found — set it in Settings";

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
        if (value is null) { CanChangeFramework = false; InstallAction = "Install"; IsDowngrade = false; IsFrameworkInstall = false; IsFrameworkUpdate = false; IsFrameworkReinstall = true; return; }
        Changelog.Add(value);

        CanChangeFramework = VersionService.LauncherSupported(value.MinLauncherVersion, LauncherVersion);
        if (!CanChangeFramework) { InstallAction = "Update launcher first"; IsDowngrade = false; IsFrameworkInstall = false; IsFrameworkUpdate = false; IsFrameworkReinstall = true; return; }

        if (_installedFramework is null)
            { InstallAction = $"Install v{value.Version}"; IsDowngrade = false; IsFrameworkInstall = true; IsFrameworkUpdate = false; IsFrameworkReinstall = false; }
        else if (VersionService.IsNewer(value.Version, _installedFramework))
            { InstallAction = $"Update to v{value.Version}"; IsDowngrade = false; IsFrameworkInstall = false; IsFrameworkUpdate = true; IsFrameworkReinstall = false; }
        else if (VersionService.IsNewer(_installedFramework, value.Version))
            { InstallAction = $"Downgrade to v{value.Version}"; IsDowngrade = true; IsFrameworkInstall = false; IsFrameworkUpdate = false; IsFrameworkReinstall = true; }
        else
            { InstallAction = $"Reinstall v{value.Version}"; IsDowngrade = false; IsFrameworkInstall = false; IsFrameworkUpdate = false; IsFrameworkReinstall = true; }
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

            // Steam install (Windows): launch via Steam so the game gets a real session.
            var steamAppId = _platform.IsWindows ? TryGetSteamAppId(GameMini) : null;
            var exe = StarLauncherExe(GameMini);
            var request = new LaunchRequest(exe, cfg.Runner, cfg.WinePrefix, _detector.DetectUmu(),
                Esync: cfg.Esync, Fsync: cfg.Fsync, FpsOverlay: cfg.FpsOverlay, DxvkNvapi: cfg.DxvkNvapi,
                StellarPerf: cfg.StellarPerf, SteamAppId: steamAppId);
            StatusLine = "launching…";
            var proc = _launcher.Launch(request);

            if (steamAppId is not null) { StatusLine = "launching via Steam…"; return; }   // Steam hands off — nothing to monitor
            if (proc is null) { StatusLine = "launch failed: process did not start"; return; }

            // First launch / post-update: BepInEx regenerates IL2CPP interop assemblies (2-3 min) with
            // no visible window. Surface live progress from the interop dir instead of a silent wait.
            if (GameMini is { } gm && _interop.RegenExpected(gm))
            {
                await MonitorInteropGenerationAsync(gm, proc);
                return;
            }

            // Give the runner a moment; if it dies fast, surface the failure instead of a frozen "launching…".
            await Task.Delay(2500);
            StatusLine = proc.HasExited && proc.ExitCode != 0
                ? $"launch failed (runner exited {proc.ExitCode}) — check Runner / WINEPREFIX in Settings"
                : "game running";
        }
        catch (Exception ex) { StatusLine = $"launch failed: {ex.Message}"; }
    }

    // Watch BepInEx regenerate its IL2CPP interop assemblies, driving the footer progress bar from the
    // interop dir's file count. Completes when the file set settles (generation done → the game proceeds
    // to open its window) or the process exits. Console-independent: works with the BepInEx console off.
    private async Task MonitorInteropGenerationAsync(string gameMiniDir, System.Diagnostics.Process proc)
    {
        const int settleSeconds = 10;    // file writes quiet this long ⇒ generation finished
        const int timeoutSeconds = 600;  // safety cap so a stall never freezes the status forever
        const int defaultEstimate = 190; // first-ever run has no prior count to estimate from

        var target = _cfg.LastInteropCount > 0 ? _cfg.LastInteropCount : defaultEstimate;
        var launchedAt = new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero);
        DateTimeOffset? genStartedAt = null;   // set when the first interop assembly is WRITTEN this run
        try
        {
            while (true)
            {
                var snap = _interop.Snapshot(gameMiniDir);

                // Files only land in a BURST at the very end: BepInEx's Cpp2IL + Il2CppInteropGen run
                // in-memory for most of the 2-3 min (the slow part — no .dll written), then write all ~190
                // at once. So we have two honest phases. Phase 1 (no assembly written since launch yet):
                // an INDETERMINATE "preparing…" bar with elapsed — the game is still starting / generating
                // in-memory, and we'd be lying to show a "0/~190" count. Phase 2 (assemblies landing): the
                // determinate N/~target count. A pre-existing stale set from a prior run never counts as fresh.
                var freshWrite = snap.NewestWriteUtc is { } nw && nw >= launchedAt;
                if (genStartedAt is null && !freshWrite)
                {
                    IsPreparingLaunch = true;             // show the bar…
                    LaunchProgressIndeterminate = true;   // …but animated, since there's no count yet
                    var waited = DateTimeOffset.UtcNow - launchedAt;
                    StatusLine = $"First launch/update — preparing game interop (can take a few minutes)…  ({waited:m\\:ss})";
                }
                else
                {
                    genStartedAt ??= DateTimeOffset.UtcNow;
                    IsPreparingLaunch = true;             // assemblies landing → determinate progress
                    LaunchProgressIndeterminate = false;
                    var genElapsed = DateTimeOffset.UtcNow - genStartedAt.Value;
                    DownloadPercent = target > 0 ? Math.Min(99, snap.Count * 100.0 / target) : 0;
                    StatusLine = $"First launch/update — generating game interop: {snap.Count}/~{target}  ({genElapsed:m\\:ss})";

                    // Done: nothing written for the settle window after generation actually started.
                    if (snap.NewestWriteUtc is { } w && (DateTimeOffset.UtcNow - w).TotalSeconds >= settleSeconds)
                    {
                        _cfg.LastInteropCount = snap.Count;
                        _settings.Save(_cfg);
                        DownloadPercent = 100;
                        StatusLine = "interop ready — game window opening…";
                        break;
                    }
                }

                if (proc.HasExited)
                {
                    StatusLine = proc.ExitCode == 0
                        ? "game running"
                        : $"launch failed (runner exited {proc.ExitCode}) — check Runner / WINEPREFIX in Settings";
                    break;
                }
                if ((DateTimeOffset.UtcNow - launchedAt).TotalSeconds >= timeoutSeconds) { StatusLine = "game running"; break; }
                await Task.Delay(1000);
            }
        }
        finally { IsPreparingLaunch = false; LaunchProgressIndeterminate = false; }
    }

    // Resolve the executable to launch, supporting both install layouts:
    //  • Official StarLauncher: game_mini → release_x → game → StarLauncher\StarLauncher.exe (3 parents up).
    //  • Flat (e.g. Steam "…\Blue Protocol Star Resonance"): the dir IS the install — no StarLauncher.exe.
    //    The Unity player exe is "<X>.exe" beside the "<X>_Data" folder (StarSEA_STEAM_Data → StarSEA_STEAM.exe);
    //    the doorstop proxy (winhttp.dll) already lives here, so launching that exe directly loads the mod.
    private static string StarLauncherExe(string gameMini)
    {
        var starLauncherDir = Directory.GetParent(gameMini)?.Parent?.Parent?.FullName;
        if (starLauncherDir is not null)
        {
            var official = Path.Combine(starLauncherDir, "StarLauncher.exe");
            if (File.Exists(official)) return official;
        }

        if (Directory.Exists(gameMini))
        {
            var dataDir = Directory.EnumerateDirectories(gameMini, "*_Data").FirstOrDefault();
            if (dataDir is not null)
            {
                var stem = Path.GetFileName(dataDir);
                stem = stem[..^"_Data".Length];
                var gameExe = Path.Combine(gameMini, stem + ".exe");
                if (File.Exists(gameExe)) return gameExe;
            }
        }

        // Nothing resolved — return the official path so any error names a sensible target.
        return Path.Combine(starLauncherDir ?? gameMini, "StarLauncher.exe");
    }

    // If gameMini is a Steam install (…\steamapps\common\<Game>), find its app id from the sibling
    // appmanifest_<id>.acf so we can launch via Steam. Null when it isn't a Steam library path.
    private static string? TryGetSteamAppId(string gameMini)
    {
        var common = Directory.GetParent(gameMini.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var steamapps = common?.Parent;
        if (common is null || steamapps is null) return null;
        if (!string.Equals(common.Name, "common", StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.Equals(steamapps.Name, "steamapps", StringComparison.OrdinalIgnoreCase)) return null;

        var leaf = Path.GetFileName(gameMini.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        try
        {
            foreach (var acf in Directory.EnumerateFiles(steamapps.FullName, "appmanifest_*.acf"))
                if (SteamAcf.AppIdForInstallDir(File.ReadAllText(acf), leaf) is { } id) return id;
        }
        catch { /* unreadable library — fall back to direct launch */ }
        return null;
    }
}

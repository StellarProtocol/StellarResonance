using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly IGameLocator _locator;
    private readonly IGameDetector _detector;

    [ObservableProperty] private string? _gameMiniDir;
    [ObservableProperty] private string? _runner;
    [ObservableProperty] private string? _winePrefix;
    [ObservableProperty] private string _detectStatus = "";
    [ObservableProperty] private bool _testingChannel;
    [ObservableProperty] private bool _debugLogging;   // off = prod (fast); on = game console + crash-flush logging

    public string LauncherVersionLabel => $"Launcher v{HomeViewModel.LauncherVersion}";

    // Linux launch tweaks
    public bool IsLinux { get; }
    [ObservableProperty] private bool _esync;
    [ObservableProperty] private bool _fsync;
    [ObservableProperty] private bool _fpsOverlay;
    [ObservableProperty] private bool _stellarPerf;
    [ObservableProperty] private bool _dxvkNvapi;

    public SettingsViewModel(ISettingsStore settings, IGameLocator locator, IGameDetector detector, IPlatformInfo platform)
    {
        _settings = settings; _locator = locator; _detector = detector;
        IsLinux = !platform.IsWindows;
        var cfg = settings.Load();
        _gameMiniDir = cfg.GameMiniDir; _runner = cfg.Runner; _winePrefix = cfg.WinePrefix;
        _testingChannel = ChannelManifests.IsTesting(cfg.Channel);
        _debugLogging = cfg.DebugLogging;
        _esync = cfg.Esync; _fsync = cfg.Fsync; _fpsOverlay = cfg.FpsOverlay;
        _stellarPerf = cfg.StellarPerf; _dxvkNvapi = cfg.DxvkNvapi;
        if (string.IsNullOrWhiteSpace(_gameMiniDir)) RunDetect();   // auto-detect on first open
    }

    [RelayCommand]
    private void Detect() => RunDetect();

    private void RunDetect()
    {
        var found = _detector.Detect();
        if (found.Count == 0)
        {
            DetectStatus = "No game install found automatically — use Browse to pick game_mini.";
            return;
        }
        GameMiniDir = found[0];

        // Derive the WINEPREFIX from the game path, and probe for a Wine/Proton runner.
        // Only fill blanks so we never clobber a value the user set deliberately.
        var bits = new System.Collections.Generic.List<string> { "game" };
        if (string.IsNullOrWhiteSpace(WinePrefix))
        {
            var prefix = GameDetector.WinePrefixFor(found[0]);
            if (prefix is not null) { WinePrefix = prefix; bits.Add("prefix"); }
        }
        if (string.IsNullOrWhiteSpace(Runner))
        {
            var runner = _detector.DetectRunner();
            if (runner is not null) { Runner = runner; bits.Add("runner"); }
        }

        var what = string.Join(" + ", bits);
        DetectStatus = found.Count == 1
            ? $"✓ Auto-detected {what}."
            : $"Found {found.Count} installs — picked the first ({what}); Browse to choose another.";
    }

    // Every Settings field auto-saves on change — there is no Save button. The ctor sets the backing
    // fields directly (not via the property setters), so none of these fire during initialization.
    partial void OnGameMiniDirChanged(string? value) => Persist();
    partial void OnRunnerChanged(string? value) => Persist();
    partial void OnWinePrefixChanged(string? value) => Persist();
    partial void OnEsyncChanged(bool value) => Persist();
    partial void OnFsyncChanged(bool value) => Persist();
    partial void OnFpsOverlayChanged(bool value) => Persist();
    partial void OnStellarPerfChanged(bool value) => Persist();
    partial void OnDxvkNvapiChanged(bool value) => Persist();
    partial void OnTestingChannelChanged(bool value) => Persist();
    partial void OnDebugLoggingChanged(bool value) => Persist();

    private void Persist()
    {
        var cfg = _settings.Load();
        cfg.GameMiniDir = GameMiniDir; cfg.Runner = Runner; cfg.WinePrefix = WinePrefix;
        cfg.Channel = TestingChannel ? "testing" : "stable";
        cfg.DebugLogging = DebugLogging;
        cfg.Esync = Esync; cfg.Fsync = Fsync; cfg.FpsOverlay = FpsOverlay;
        cfg.StellarPerf = StellarPerf; cfg.DxvkNvapi = DxvkNvapi;
        _settings.Save(cfg);
    }

    public void DetectFrom(string gameRoot)
    {
        var found = _locator.FindGameMini(gameRoot);
        if (found is not null) GameMiniDir = found;
    }

    /// <summary>
    /// Set the game path from a Browse pick: if the user chose the StarLauncher\game root,
    /// auto-resolve to the newest release_*/game_mini; otherwise use the folder as-is
    /// (e.g. they picked game_mini directly).
    /// </summary>
    public void SetGameFromPicked(string path) => GameMiniDir = _locator.FindGameMini(path) ?? path;
}

using System.Linq;
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
    [ObservableProperty] private bool _autoUpdateBeforeLaunch;   // update framework + plugins before launch; ON by default

    public string LauncherVersionLabel => $"Launcher v{HomeViewModel.LauncherVersion}";

    // Linux launch tweaks
    public bool IsLinux { get; }
    [ObservableProperty] private bool _esync;
    [ObservableProperty] private bool _fsync;
    // Performance overlay tri-state (ComboBox index): 0 Off · 1 FPS counter (DXVK) · 2 Full (MangoHud).
    [ObservableProperty] private int _perfOverlayIndex;
    [ObservableProperty] private bool _mangoHudMissing;   // hint under the ComboBox when Full is picked but MangoHud isn't installed
    [ObservableProperty] private bool _stellarPerf;
    [ObservableProperty] private bool _dxvkNvapi;

    // Advanced (Linux power-user) launch options
    [ObservableProperty] private string? _wrapperCommand;
    [ObservableProperty] private string? _gameArguments;
    [ObservableProperty] private string? _preLaunchScript;
    [ObservableProperty] private string? _postExitScript;
    public System.Collections.ObjectModel.ObservableCollection<EnvVarRowViewModel> EnvVars { get; } = new();

    public SettingsViewModel(ISettingsStore settings, IGameLocator locator, IGameDetector detector, IPlatformInfo platform)
    {
        _settings = settings; _locator = locator; _detector = detector;
        IsLinux = !platform.IsWindows;
        var cfg = settings.Load();
        _gameMiniDir = cfg.GameMiniDir; _runner = cfg.Runner; _winePrefix = cfg.WinePrefix;
        _testingChannel = ChannelManifests.IsTesting(cfg.Channel);
        _debugLogging = cfg.DebugLogging;
        _autoUpdateBeforeLaunch = cfg.AutoUpdateBeforeLaunch;
        _esync = cfg.Esync; _fsync = cfg.Fsync;
        _perfOverlayIndex = (int)cfg.EffectiveOverlay();
        _mangoHudMissing = _perfOverlayIndex == (int)PerfOverlayMode.Full && !MangoHud.IsInstalled();
        _stellarPerf = cfg.StellarPerf; _dxvkNvapi = cfg.DxvkNvapi;
        _wrapperCommand = cfg.WrapperCommand;
        _gameArguments = cfg.GameArguments;
        _preLaunchScript = cfg.PreLaunchScript;
        _postExitScript = cfg.PostExitScript;
        foreach (var e in cfg.ExtraEnv)
            EnvVars.Add(new EnvVarRowViewModel(e, Persist, RemoveEnvVar));
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
    partial void OnPerfOverlayIndexChanged(int value)
    {
        MangoHudMissing = value == (int)PerfOverlayMode.Full && !MangoHud.IsInstalled();
        Persist();
    }
    partial void OnStellarPerfChanged(bool value) => Persist();
    partial void OnDxvkNvapiChanged(bool value) => Persist();
    partial void OnTestingChannelChanged(bool value) => Persist();
    partial void OnDebugLoggingChanged(bool value) => Persist();
    partial void OnAutoUpdateBeforeLaunchChanged(bool value) => Persist();
    partial void OnWrapperCommandChanged(string? value) => Persist();
    partial void OnGameArgumentsChanged(string? value) => Persist();
    partial void OnPreLaunchScriptChanged(string? value) => Persist();
    partial void OnPostExitScriptChanged(string? value) => Persist();

    [RelayCommand]
    private void AddEnvVar()
    {
        EnvVars.Add(new EnvVarRowViewModel(new EnvVar(), Persist, RemoveEnvVar));
        Persist();
    }

    private void RemoveEnvVar(EnvVarRowViewModel row)
    {
        EnvVars.Remove(row);
        Persist();
    }

    private void Persist()
    {
        var cfg = _settings.Load();
        cfg.GameMiniDir = GameMiniDir; cfg.Runner = Runner; cfg.WinePrefix = WinePrefix;
        cfg.Channel = TestingChannel ? "testing" : "stable";
        cfg.DebugLogging = DebugLogging;
        cfg.AutoUpdateBeforeLaunch = AutoUpdateBeforeLaunch;
        cfg.Esync = Esync; cfg.Fsync = Fsync;
        cfg.SetOverlay((PerfOverlayMode)PerfOverlayIndex);
        cfg.StellarPerf = StellarPerf; cfg.DxvkNvapi = DxvkNvapi;
        cfg.WrapperCommand = string.IsNullOrWhiteSpace(WrapperCommand) ? null : WrapperCommand;
        cfg.GameArguments = string.IsNullOrWhiteSpace(GameArguments) ? null : GameArguments;
        cfg.PreLaunchScript = string.IsNullOrWhiteSpace(PreLaunchScript) ? null : PreLaunchScript;
        cfg.PostExitScript = string.IsNullOrWhiteSpace(PostExitScript) ? null : PostExitScript;
        cfg.ExtraEnv = EnvVars
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => r.ToModel())
            .ToList();
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

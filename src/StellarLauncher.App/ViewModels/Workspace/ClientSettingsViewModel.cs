using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Launch;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.ViewModels.Workspace;

/// <summary>Everything about this one install (mockup #settings). Every change persists immediately.</summary>
public sealed partial class ClientSettingsViewModel : ObservableObject
{
    private readonly ClientWorkspaceViewModel _ws;
    private ClientProfile C => _ws.Client;
    private bool _loading;

    public ObservableCollection<SwatchViewModel> Swatches { get; } = new();
    public ObservableCollection<EnvVarRowViewModel> EnvVars { get; } = new();

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _gameMiniDir = "";
    [ObservableProperty] private string? _runner, _winePrefix, _wrapper, _gameArgs, _preLaunch, _postExit;
    [ObservableProperty] private bool _testingChannel, _autoUpdate, _debugLogging, _dxvkNvapi, _esync, _fsync, _stellarPerf, _mangoHudMissing;
    [ObservableProperty] private int _perfOverlayIndex;
    [ObservableProperty] private string _status = "";

    public bool IsLinux => !_ws.Services.Platform.IsWindows;
    public string LayoutLine
    {
        get
        {
            var tag = ClientNaming.LayoutTag(ClientNaming.DetectLayout(GameMiniDir));
            var release = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(GameMiniDir.TrimEnd('/', '\\')) ?? "");
            var exe = System.IO.Path.GetFileName(GameExeResolver.StarLauncherExe(_ws.Services.Fs, GameMiniDir));
            return $"Detected layout: {(tag.Length > 0 ? tag : "unknown")} · {release} · {exe}";
        }
    }
    public string UmuLine => _ws.Services.Detector.DetectUmu() is { } u ? $"umu-run found at {u} — Proton launches through it." : "umu-run not found — Proton falls back to its run verb.";

    public ClientSettingsViewModel(ClientWorkspaceViewModel ws)
    {
        _ws = ws;
        Load();
    }

    private void Load()
    {
        _loading = true;
        Name = C.Name; GameMiniDir = C.GameMiniDir;
        TestingChannel = C.Channel == "testing"; AutoUpdate = C.AutoUpdateBeforeLaunch; DebugLogging = C.DebugLogging;
        var lx = C.Linux;
        Runner = lx?.Runner; WinePrefix = lx?.WinePrefix; DxvkNvapi = lx?.DxvkNvapi ?? true; Esync = lx?.Esync ?? true; Fsync = lx?.Fsync ?? true;
        PerfOverlayIndex = (int)(lx?.OverlayMode() ?? PerfOverlayMode.Off); StellarPerf = lx?.StellarPerf ?? false;
        MangoHudMissing = PerfOverlayIndex == (int)PerfOverlayMode.Full && !MangoHud.IsInstalled();
        Wrapper = C.Advanced.Wrapper; GameArgs = C.Advanced.GameArgs; PreLaunch = C.Advanced.PreLaunch; PostExit = C.Advanced.PostExit;
        EnvVars.Clear();
        foreach (var e in C.Advanced.Env) EnvVars.Add(new EnvVarRowViewModel(e, Persist, RemoveEnvVar));
        Swatches.Clear();
        foreach (var hex in AccentPalette.Defaults) Swatches.Add(new SwatchViewModel(hex, string.Equals(hex, C.Accent, StringComparison.OrdinalIgnoreCase), PickAccent));
        _loading = false;
        OnPropertyChanged(nameof(LayoutLine)); OnPropertyChanged(nameof(UmuLine));
    }

    public void PickAccent(string hex)
    {
        C.Accent = hex;
        foreach (var s in Swatches) s.IsSelected = string.Equals(s.Hex, hex, StringComparison.OrdinalIgnoreCase);
        PersistIdentity();
    }

    partial void OnNameChanged(string value)
    {
        if (_loading) return;
        var trimmed = value.Trim();
        if (trimmed.Length == 0) { Status = "name cannot be empty"; return; }
        if (_ws.Shell.Config.NameTaken(trimmed, exceptId: C.Id)) { Status = $"'{trimmed}' is already a client name"; return; }
        C.Name = trimmed; Status = ""; PersistIdentity();
    }

    partial void OnGameMiniDirChanged(string value) { if (!_loading) { C.GameMiniDir = value; Persist(); OnPropertyChanged(nameof(LayoutLine)); } }
    partial void OnTestingChannelChanged(bool value) { if (!_loading) { C.Channel = value ? "testing" : "stable"; PersistIdentity(); } }
    partial void OnAutoUpdateChanged(bool value) { if (!_loading) { C.AutoUpdateBeforeLaunch = value; Persist(); } }
    partial void OnDebugLoggingChanged(bool value) { if (!_loading) { C.DebugLogging = value; Persist(); } }
    partial void OnRunnerChanged(string? value) { if (!_loading) { Linux().Runner = value; Persist(); } }
    partial void OnWinePrefixChanged(string? value) { if (!_loading) { Linux().WinePrefix = value; Persist(); } }
    partial void OnDxvkNvapiChanged(bool value) { if (!_loading) { Linux().DxvkNvapi = value; Persist(); } }
    partial void OnEsyncChanged(bool value) { if (!_loading) { Linux().Esync = value; Persist(); } }
    partial void OnFsyncChanged(bool value) { if (!_loading) { Linux().Fsync = value; Persist(); } }
    partial void OnStellarPerfChanged(bool value) { if (!_loading) { Linux().StellarPerf = value; Persist(); } }
    partial void OnPerfOverlayIndexChanged(int value)
    {
        MangoHudMissing = value == (int)PerfOverlayMode.Full && !MangoHud.IsInstalled();
        if (_loading) return;
        Linux().PerfOverlay = value switch { 1 => "fps", 2 => "full", _ => "off" }; Persist();
    }
    partial void OnWrapperChanged(string? value) { if (!_loading) { C.Advanced.Wrapper = Blank(value); Persist(); } }
    partial void OnGameArgsChanged(string? value) { if (!_loading) { C.Advanced.GameArgs = Blank(value); Persist(); } }
    partial void OnPreLaunchChanged(string? value) { if (!_loading) { C.Advanced.PreLaunch = Blank(value); Persist(); } }
    partial void OnPostExitChanged(string? value) { if (!_loading) { C.Advanced.PostExit = Blank(value); Persist(); } }

    private LinuxRuntime Linux() => C.Linux ??= new LinuxRuntime();
    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    [RelayCommand] private void AddEnvVar() { EnvVars.Add(new EnvVarRowViewModel(new EnvVar(), Persist, RemoveEnvVar)); Persist(); }
    private void RemoveEnvVar(EnvVarRowViewModel row) { EnvVars.Remove(row); Persist(); }

    private void Persist()
    {
        C.Advanced.Env = EnvVars.Where(r => !string.IsNullOrWhiteSpace(r.Name)).Select(r => r.ToModel()).ToList();
        _ws.SaveProfile();
    }

    // name / accent / channel are shown in the rail → rebuild rows too
    private void PersistIdentity() { Persist(); _ws.Shell.ClientEdited(); }

    [RelayCommand]
    private void Detect()
    {
        var found = _ws.Services.Detector.Detect();
        if (found.Count == 0) { Status = "No game install found automatically — use Browse."; return; }
        var pick = found.FirstOrDefault(p => ClientCandidates.SamePath(p, GameMiniDir, _ws.Services.Platform.IsWindows)) ?? found[0];
        GameMiniDir = pick;
        if (IsLinux && string.IsNullOrWhiteSpace(WinePrefix)) WinePrefix = GameDetector.WinePrefixFor(pick);
        if (IsLinux && string.IsNullOrWhiteSpace(Runner)) Runner = _ws.Services.Detector.DetectRunner();
        Status = found.Count == 1 ? "✓ detected" : $"found {found.Count} installs — picked {pick}";
    }

    public void SetGameFromPicked(string path) => GameMiniDir = _ws.Services.Locator.FindGameMini(path) ?? path;

    [RelayCommand]
    private async Task RemoveClientAsync()
    {
        if (!await _ws.Services.Confirm.AskAsync($"Remove {C.Name} from the launcher?", "Only the launcher entry is forgotten. Nothing under the game folder is deleted; re-adding the folder restores it.", "Remove")) return;
        _ws.Shell.ClientRemoved(C.Id);
    }
}

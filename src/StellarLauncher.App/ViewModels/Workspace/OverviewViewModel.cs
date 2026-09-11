using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.App.Services;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.ViewModels.Workspace;

public sealed partial class OverviewViewModel : ObservableObject
{
    private readonly ClientWorkspaceViewModel _ws;
    private bool _loading;

    public ObservableCollection<VersionManifest> Versions { get; } = new();
    [ObservableProperty] private VersionManifest? _selectedVersion;
    [ObservableProperty] private string _actionLabel = "Install";
    [ObservableProperty] private bool _canChangeFramework;
    [ObservableProperty] private bool _isInstall, _isUpdate, _isReinstall, _isDowngrade, _confirmVisible;
    [ObservableProperty] private bool _modded, _autoUpdate, _debugLogging;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _onOthersLine = "";

    public OverviewViewModel(ClientWorkspaceViewModel ws)
    {
        _ws = ws;
        _ws.Refreshed += Load;
        Load();
    }

    public string InstalledLabel => _ws.Inventory.FrameworkVersion is { } v ? $"v{v}" : "not installed";
    public string ChannelLabel => _ws.IsTesting ? "testing (stable releases included)" : "stable";
    public string LatestLabel => _ws.Manifest is { } m ? $"v{m.Latest} · {m.Versions.FirstOrDefault(v => v.Version == m.Latest)?.Date}" : "offline";
    public string LastLaunchLabel => _ws.Session.StartedAt is { } t ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "never this session";
    public string PreviousExitLabel => _ws.Session.ExitCode switch { null => "—", 0 => "clean", var c => $"code {c}" };
    public string InteropLabel => _ws.Client.LastInteropCount > 0 ? $"{_ws.Client.LastInteropCount} assemblies" : "not generated yet";
    public int InstalledCount => _ws.Inventory.InstalledCount;
    public string UpdatesLine => _ws.UpdatesBadge > 0 ? $"{_ws.UpdatesBadge} available" : "none";
    public int DisabledCount => _ws.Inventory.DisabledCount;
    public string MarkerLine => _ws.Inventory.FrameworkVersion is { } v ? $"{v} ✓" : "not installed";
    public string DuplicateLine => _ws.Inventory.DuplicateSlots.Count == 0 ? "none"
        : "⚠ " + string.Join("; ", _ws.Inventory.DuplicateSlots.Select(d => string.Join(" + ", d.Dirs.Select(System.IO.Path.GetFileName))));
    public bool HasDuplicates => _ws.Inventory.DuplicateSlots.Count > 0;
    public string LogSizeLine => _ws.Inventory.LogBytes > 0 ? $"{_ws.Inventory.LogBytes / 1024.0 / 1024.0:0.0} MB" : "no log yet";

    private void Load()
    {
        _loading = true;
        Modded = _ws.Client.Modded; AutoUpdate = _ws.Client.AutoUpdateBeforeLaunch; DebugLogging = _ws.Client.DebugLogging;
        Versions.Clear();
        if (_ws.Manifest is { } m) foreach (var v in m.Versions) Versions.Add(v);
        SelectedVersion = Versions.FirstOrDefault(v => v.Version == _ws.Manifest?.Latest) ?? Versions.FirstOrDefault();
        _loading = false;
        _ = LoadOnOthersAsync();
        foreach (var p in new[] { nameof(InstalledLabel), nameof(ChannelLabel), nameof(LatestLabel), nameof(LastLaunchLabel), nameof(PreviousExitLabel),
                     nameof(InteropLabel), nameof(InstalledCount), nameof(UpdatesLine), nameof(DisabledCount), nameof(MarkerLine), nameof(DuplicateLine),
                     nameof(HasDuplicates), nameof(LogSizeLine) })
            OnPropertyChanged(p);
    }

    // Plugins other clients have that this one lacks (spec § 5.3).
    private async Task LoadOnOthersAsync()
    {
        try
        {
            var here = _ws.Inventory.Plugins.Where(p => p.Present).Select(p => p.Entry.Id).ToHashSet();
            var names = new SortedSet<string>();
            foreach (var other in _ws.Shell.Config.Clients.Where(c => c.Id != _ws.Client.Id))
            {
                var reg = await _ws.Services.Core.Registry.ForChannelAsync(other.Channel, CancellationToken.None);
                foreach (var p in _ws.Services.Core.Inventory.Read(other, reg).Plugins.Where(p => p.Present && !here.Contains(p.Entry.Id)))
                    names.Add(p.Entry.Name);
            }
            OnOthersLine = names.Count == 0 ? "nothing" : string.Join(", ", names);
        }
        catch (Exception) { OnOthersLine = "unavailable"; }   // fire-and-forget from Load(): never leave an unobserved fault
    }

    partial void OnSelectedVersionChanged(VersionManifest? value)
    {
        ConfirmVisible = false;
        var installed = _ws.Inventory.FrameworkVersion;
        (IsInstall, IsUpdate, IsReinstall, IsDowngrade) = (false, false, false, false);
        if (value is null) { CanChangeFramework = false; ActionLabel = "Install"; return; }
        CanChangeFramework = VersionService.LauncherSupported(value.MinLauncherVersion, AppInfo.LauncherVersion);
        // Too-old launcher: keep ONE (disabled) button visible so the label explains why nothing can be installed.
        if (!CanChangeFramework) { ActionLabel = "Update launcher first"; IsReinstall = true; return; }
        if (installed is null) { ActionLabel = $"Install v{value.Version}"; IsInstall = true; }
        else if (VersionService.IsNewer(value.Version, installed)) { ActionLabel = $"Update to v{value.Version}"; IsUpdate = true; }
        else if (VersionService.IsNewer(installed, value.Version)) { ActionLabel = $"Downgrade to v{value.Version}"; IsReinstall = true; IsDowngrade = true; }
        else { ActionLabel = $"Reinstall v{value.Version}"; IsReinstall = true; }
    }

    [RelayCommand] private void RequestChange() { if (CanChangeFramework) ConfirmVisible = true; }
    [RelayCommand] private void CancelChange() => ConfirmVisible = false;

    [RelayCommand]
    private async Task ConfirmChangeAsync()
    {
        ConfirmVisible = false;
        if (SelectedVersion is not { } target) return;
        try
        {
            using var buffered = new MemoryStream();
            long lastTick = -1;
            var progress = new Progress<DownloadProgress>(p =>
            {
                long tick = p.Fraction is { } f ? (long)(f * 100) : p.BytesRead >> 20;
                if (tick != lastTick) { lastTick = tick; Status = DownloadStatus.Line($"downloading v{target.Version}…", p); }
            });
            await _ws.Services.Core.Install.Http.DownloadToAsync(new Uri(target.BundleUrl), buffered, progress);
            buffered.Position = 0;
            Status = $"installing v{target.Version}…";
            await _ws.Services.Core.Install.Installer.InstallAsync(buffered, target.Sha256, _ws.Client.GameMiniDir, target.Version);
            Status = $"installed v{target.Version}";
            await _ws.RefreshAsync();
        }
        catch (Exception ex) { Status = $"install failed: {ex.Message}"; }
    }

    partial void OnModdedChanged(bool value)
    {
        if (_loading) return;
        _ws.Client.Modded = value; _ws.SaveProfile();
        var path = System.IO.Path.Combine(_ws.Client.GameMiniDir, "doorstop_config.ini");
        if (_ws.Services.Fs.File.Exists(path)) _ws.Services.Doorstop.SetEnabled(path, value);
    }
    partial void OnAutoUpdateChanged(bool value) { if (!_loading) { _ws.Client.AutoUpdateBeforeLaunch = value; _ws.SaveProfile(); } }
    partial void OnDebugLoggingChanged(bool value) { if (!_loading) { _ws.Client.DebugLogging = value; _ws.SaveProfile(); } }

    [RelayCommand] private void OpenPlugins() => _ws.ShowPluginsCommand.Execute(null);
    [RelayCommand] private void OpenLogs() => _ws.ShowLogsCommand.Execute(null);
}

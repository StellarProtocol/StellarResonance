using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels.Shell;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Launch;
using StellarLauncher.Core.Logs;
using StellarLauncher.Core.Model;

namespace StellarLauncher.App.ViewModels.Workspace;

/// <summary>One client's console: header (identity + live state + launch), four tabs (mockup #overview).</summary>
public sealed partial class ClientWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly WorkspaceTabFactories _tabs;
    private readonly Dictionary<WorkspaceTab, object> _tabCache = new();
    private readonly Action<LaunchSession> _onSession;
    private bool _disposed;

    public ShellViewModel Shell { get; }
    public ClientProfile Client { get; }
    public WorkspaceServices Services { get; }
    public LaunchSession Session { get; }

    public InventorySnapshot Inventory { get; private set; } = InventorySnapshot.Missing;
    public IReadOnlyList<PluginEntry> Registry { get; private set; } = Array.Empty<PluginEntry>();
    public FrameworkManifest? Manifest { get; private set; }
    public IReadOnlyList<LogCallout> Callouts { get; private set; } = Array.Empty<LogCallout>();
    public string? NewerGameMini { get; private set; }

    public event Action? Refreshed;

    [ObservableProperty] private WorkspaceTab _tab = WorkspaceTab.Overview;
    [ObservableProperty] private object? _tabContent;
    [ObservableProperty] private string _status = "";

    public ClientWorkspaceViewModel(ShellViewModel shell, ClientProfile client, WorkspaceServices services, WorkspaceTabFactories tabs)
    {
        Shell = shell; Client = client; Services = services; _tabs = tabs;
        Session = shell.Sessions.For(client);
        _onSession = s => { if (s.ClientId == client.Id) RaiseHeader(); };
        shell.Sessions.SessionChanged += _onSession;
        ShowTab(WorkspaceTab.Overview);
        _ = RefreshAsync();
    }

    /// <summary>Called by the shell when this page is navigated away from. Cascades to every tab VM built so far.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shell.Sessions.SessionChanged -= _onSession;
        foreach (var d in _tabCache.Values.OfType<IDisposable>()) d.Dispose();
        _tabCache.Clear();
        Refreshed = null;   // tab VMs subscribe Load() here; drop them so nothing stays rooted through this page
    }

    // ---- header ----
    public string Name => Client.Name;
    public string Path => Client.GameMiniDir;
    public IBrush AccentBrush => AccentBrushes.Solid(Client.Accent);
    public IBrush AccentSoftBrush => AccentBrushes.Soft(Client.Accent);
    public IBrush AccentGlowBrush => AccentBrushes.Glow(Client.Accent);
    public string ChannelTag => Client.Channel == "testing" ? "Testing" : "Stable";
    public bool IsTesting => Client.Channel == "testing";
    public string ModeTag => Client.Modded ? "Modded" : "Vanilla";
    public string RuntimeTag => Client.Linux?.Runner is { } r
        ? $"Linux · {System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(r) ?? r)}" : "Windows";
    public string StateLine => SessionPresenter.StateLine(Session, Inventory, Client.Modded);
    public IBrush StateBrush => SessionPresenter.StateBrush(Session, Inventory);
    public bool ShowProgress => Session.State is SessionState.Launching or SessionState.Preparing;
    public double Progress => (Session.Progress ?? 0) * 100;
    public bool ProgressIndeterminate => Session.ProgressIndeterminate;
    public bool IsLaunchVisible => Session.CanLaunch && Inventory.FolderExists;
    public bool IsRunningVisible => Session.State is SessionState.Running or SessionState.SteamHandoff;
    public bool IsPrepVisible => Session.State is SessionState.Launching or SessionState.Preparing;
    public bool CanStop => Session.CanStop;
    public int PluginsBadge => Inventory.InstalledCount;
    public int UpdatesBadge { get; private set; }
    public bool HasUpdates => UpdatesBadge > 0;
    public int LogsBadge => Callouts.Count;
    public bool HasCallouts => LogsBadge > 0;

    // ---- tabs ----
    public bool IsOverview => Tab == WorkspaceTab.Overview;
    public bool IsPlugins => Tab == WorkspaceTab.Plugins;
    public bool IsSettings => Tab == WorkspaceTab.Settings;
    public bool IsLogs => Tab == WorkspaceTab.Logs;
    // The active tab's underline is the client accent; the view binds these (a Style setter could not win over a
    // local BorderBrush binding, so the VM decides).
    public IBrush OverviewUnderline => Underline(WorkspaceTab.Overview);
    public IBrush PluginsUnderline => Underline(WorkspaceTab.Plugins);
    public IBrush SettingsUnderline => Underline(WorkspaceTab.Settings);
    public IBrush LogsUnderline => Underline(WorkspaceTab.Logs);
    private IBrush Underline(WorkspaceTab tab) => Tab == tab ? AccentBrush : Brushes.Transparent;

    [RelayCommand] private void ShowOverview() => ShowTab(WorkspaceTab.Overview);
    [RelayCommand] private void ShowPlugins() => ShowTab(WorkspaceTab.Plugins);
    [RelayCommand] private void ShowSettings() => ShowTab(WorkspaceTab.Settings);
    [RelayCommand] private void ShowLogs() => ShowTab(WorkspaceTab.Logs);

    private void ShowTab(WorkspaceTab tab)
    {
        Tab = tab;
        if (!_tabCache.TryGetValue(tab, out var content))
        {
            content = tab switch
            {
                WorkspaceTab.Plugins => _tabs.Plugins(this),
                WorkspaceTab.Settings => _tabs.Settings(this),
                WorkspaceTab.Logs => _tabs.Logs(this),
                _ => _tabs.Overview(this),
            };
            _tabCache[tab] = content;
        }
        TabContent = content;
        foreach (var p in new[] { nameof(IsOverview), nameof(IsPlugins), nameof(IsSettings), nameof(IsLogs),
                     nameof(OverviewUnderline), nameof(PluginsUnderline), nameof(SettingsUnderline), nameof(LogsUnderline) })
            OnPropertyChanged(p);
    }

    // ---- data ----
    [RelayCommand]
    public async Task RefreshAsync()
    {
        try
        {
            Registry = await Services.Core.Registry.ForChannelAsync(Client.Channel, CancellationToken.None);
            Inventory = Services.Core.Inventory.Read(Client, Registry);
            Manifest = await Services.Core.Manifests.LatestForAsync(Client.Channel, CancellationToken.None);
            var col = new Core.Matrix.ClientColumn(Client, Inventory, Registry);
            UpdatesBadge = Registry.Count(e => Core.Matrix.PluginMatrixBuilder.Classify(col, e.Id).Kind == Core.Matrix.MatrixCellKind.UpdateAvailable);
            NewerGameMini = FindNewerRelease();
            Callouts = LogPatterns.Scan(Array.Empty<LogLine>(), Inventory, ShadowCopyFix.Find(Services.Fs, Client.GameMiniDir), NewerGameMini);
            Status = "";
        }
        catch (Exception ex) { Status = $"offline — {ex.Message}"; }
        RaiseHeader();
        Refreshed?.Invoke();
    }

    // game_mini → release_x → game: is there a newer release_* than the configured one?
    private string? FindNewerRelease()
    {
        var gameRoot = Services.Fs.Directory.GetParent(Client.GameMiniDir)?.Parent?.FullName;
        if (gameRoot is null) return null;
        var newest = Services.Locator.FindGameMini(gameRoot);
        return newest is not null && !ClientCandidates.SamePath(newest, Client.GameMiniDir, Services.Platform.IsWindows) ? newest : null;
    }

    public void SaveProfile()
    {
        var cfg = Shell.Config;
        if (cfg.FindById(Client.Id) is { } stored && !ReferenceEquals(stored, Client))
        {
            var i = cfg.Clients.IndexOf(stored); cfg.Clients[i] = Client;
        }
        Shell.SaveConfig();
        RaiseHeader();
    }

    [RelayCommand] private Task Launch() => Shell.Sessions.LaunchAsync(Client, Services.Core.Review, CancellationToken.None);
    [RelayCommand] private void Stop() => Shell.Sessions.Stop(Client);

    private void RaiseHeader()
    {
        foreach (var p in new[] { nameof(Name), nameof(AccentBrush), nameof(AccentSoftBrush), nameof(AccentGlowBrush), nameof(ChannelTag), nameof(IsTesting),
                     nameof(ModeTag), nameof(RuntimeTag), nameof(StateLine), nameof(StateBrush), nameof(ShowProgress), nameof(Progress),
                     nameof(ProgressIndeterminate), nameof(IsLaunchVisible), nameof(IsRunningVisible), nameof(IsPrepVisible), nameof(CanStop),
                     nameof(PluginsBadge), nameof(UpdatesBadge), nameof(HasUpdates), nameof(LogsBadge), nameof(HasCallouts) })
            OnPropertyChanged(p);
    }
}

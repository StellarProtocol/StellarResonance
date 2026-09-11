using System;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.App.Services;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Launch;

namespace StellarLauncher.App.ViewModels.Dashboard;

/// <summary>One client's console on the Dashboard (mockup #dashboard): identity in the accent, state in
/// semantic colours, one primary action + Stop.</summary>
public sealed partial class ClientTileViewModel : ObservableObject
{
    private readonly Func<ClientProfile, Task> _launch;
    private readonly Action<ClientProfile> _stop;
    private readonly Action<ClientProfile> _open;
    private InventorySnapshot _inv = InventorySnapshot.Missing;
    private string? _latest;
    private int _updates;

    public ClientProfile Client { get; }
    public LaunchSession Session { get; }

    public ClientTileViewModel(ClientProfile client, LaunchSession session,
        Func<ClientProfile, Task> launch, Action<ClientProfile> stop, Action<ClientProfile> open)
    {
        Client = client; Session = session; _launch = launch; _stop = stop; _open = open;
    }

    public string Name => Client.Name;
    public string Path => Client.GameMiniDir;
    public IBrush AccentBrush => AccentBrushes.Solid(Client.Accent);
    public IBrush AccentSoftBrush => AccentBrushes.Soft(Client.Accent);
    public IBrush AccentLineBrush => AccentBrushes.Line(Client.Accent);
    public string ChannelTag => Client.Channel == "testing" ? "Testing" : "Stable";
    public bool IsTesting => Client.Channel == "testing";
    public string ModeTag => Client.Modded ? "Modded" : "Vanilla";
    public string RunnerTag => Client.Linux?.Runner is { } r
        ? System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(r) ?? r)
        : "Windows";

    public string FrameworkLine => _inv.FrameworkVersion is null ? "fw not installed"
        : _latest is not null && Core.Services.VersionService.IsNewer(_latest, _inv.FrameworkVersion) ? $"fw {_inv.FrameworkVersion} · {_latest} available"
        : $"fw {_inv.FrameworkVersion} · latest";
    public bool FrameworkNeedsAttention => _inv.FrameworkVersion is null || (_latest is not null && Core.Services.VersionService.IsNewer(_latest, _inv.FrameworkVersion));
    public bool ShowInstallFramework => _inv.FolderExists && _inv.FrameworkVersion is null;
    public string PluginsLine
    {
        get
        {
            var n = _inv.InstalledCount;
            var noun = n == 1 ? "plugin" : "plugins";
            return _updates > 0 ? $"{n} {noun} · {_updates} update{(_updates == 1 ? "" : "s")}" : $"{n} {noun} · up to date";
        }
    }

    public string StateLine => Session.State switch
    {
        SessionState.Running when Session.StartedAt is { } t => $"running · {Elapsed(t)}",
        SessionState.Running => "running",
        SessionState.Launching or SessionState.Preparing => Session.StatusText,
        SessionState.SteamHandoff => "launched via Steam",
        SessionState.Failed => Session.StatusText.Length > 0 ? Session.StatusText : "failed",
        SessionState.Exited => "exited",
        _ => !_inv.FolderExists ? "folder missing" : Client.Modded ? "idle" : "idle · launches vanilla until the framework is installed",
    };
    public IBrush StateBrush => Session.State switch
    {
        SessionState.Running or SessionState.SteamHandoff => new SolidColorBrush(Color.Parse("#54e3a0")),
        SessionState.Launching or SessionState.Preparing => new SolidColorBrush(Color.Parse("#9ec2ff")),
        SessionState.Failed => new SolidColorBrush(Color.Parse("#ff9a9a")),
        _ => !_inv.FolderExists ? new SolidColorBrush(Color.Parse("#ff9a9a")) : new SolidColorBrush(Color.Parse("#697297")),
    };

    public bool ShowProgress => Session.State is SessionState.Launching or SessionState.Preparing;
    public double Progress => (Session.Progress ?? 0) * 100;
    public bool ProgressIndeterminate => Session.ProgressIndeterminate;
    public bool IsLaunchVisible => Session.CanLaunch && _inv.FolderExists;
    public bool IsRunningVisible => Session.State is SessionState.Running or SessionState.SteamHandoff;
    public bool IsPrepVisible => Session.State is SessionState.Launching or SessionState.Preparing;
    public bool CanStop => Session.CanStop;

    public void SetInventory(InventorySnapshot inv, string? latestFramework, int updates)
    {
        _inv = inv; _latest = latestFramework; _updates = updates; Refresh();
    }

    public void Refresh()
    {
        foreach (var p in new[] { nameof(FrameworkLine), nameof(FrameworkNeedsAttention), nameof(ShowInstallFramework), nameof(PluginsLine),
                     nameof(StateLine), nameof(StateBrush), nameof(ShowProgress), nameof(Progress), nameof(ProgressIndeterminate),
                     nameof(IsLaunchVisible), nameof(IsRunningVisible), nameof(IsPrepVisible), nameof(CanStop) })
            OnPropertyChanged(p);
    }

    [RelayCommand] private Task Launch() => _launch(Client);
    [RelayCommand] private void Stop() => _stop(Client);
    [RelayCommand] private void Open() => _open(Client);

    private static string Elapsed(DateTimeOffset since)
    {
        var d = DateTimeOffset.UtcNow - since;
        return d.TotalHours >= 1 ? $"{(int)d.TotalHours} h {d.Minutes} min" : $"{Math.Max(0, (int)d.TotalMinutes)} min";
    }
}

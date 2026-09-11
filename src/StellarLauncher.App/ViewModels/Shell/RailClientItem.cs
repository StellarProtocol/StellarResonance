using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using StellarLauncher.App.Services;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Launch;

namespace StellarLauncher.App.ViewModels.Shell;

/// <summary>One rail row: client identity (accent) + live state (semantic dot). Spec § 5.1.</summary>
public sealed partial class RailClientItem : ObservableObject
{
    private static readonly IBrush Transparent = Brushes.Transparent;
    private string? _framework;

    public ClientProfile Client { get; }
    public LaunchSession Session { get; }

    [ObservableProperty] private bool _isSelected;

    public RailClientItem(ClientProfile client, LaunchSession session) { Client = client; Session = session; }

    public string Name => Client.Name;
    public IBrush AccentBrush => AccentBrushes.Solid(Client.Accent);
    public IBrush NameBrush => IsSelected ? Brushes.White : AccentBrush;
    public IBrush RowBackground => IsSelected ? AccentBrushes.Soft(Client.Accent) : Transparent;
    public IBrush RowBorder => IsSelected ? AccentBrush : Transparent;
    public bool ShowQuickLaunch => Session.CanLaunch && Session.State != SessionState.SteamHandoff;

    public string StateLabel => Session.State switch
    {
        SessionState.Launching => "launching",
        SessionState.Preparing => "preparing",
        SessionState.Running => "running",
        SessionState.SteamHandoff => "via Steam",
        SessionState.Failed => Session.ExitCode is { } c ? $"exited ({c})" : "failed",
        SessionState.Exited => "exited",
        _ => "idle",
    };

    public IBrush DotBrush => Session.State switch
    {
        SessionState.Running or SessionState.SteamHandoff => new SolidColorBrush(Color.Parse("#54e3a0")),
        SessionState.Launching or SessionState.Preparing => new SolidColorBrush(Color.Parse("#5b8cff")),
        SessionState.Failed => new SolidColorBrush(Color.Parse("#ff6b6b")),
        _ => new SolidColorBrush(Color.Parse("#2a3050")),
    };

    public string Summary => $"{Client.Channel} · fw {_framework ?? "none"} · {StateLabel}";
    public string? SummaryFramework => _framework;

    public void SetFramework(string? version) { _framework = version; OnPropertyChanged(nameof(Summary)); }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(AccentBrush)); OnPropertyChanged(nameof(NameBrush));
        OnPropertyChanged(nameof(StateLabel)); OnPropertyChanged(nameof(DotBrush)); OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ShowQuickLaunch));
    }

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(NameBrush)); OnPropertyChanged(nameof(RowBackground)); OnPropertyChanged(nameof(RowBorder));
    }
}

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.App.Services;
using StellarLauncher.Core.Clients;

namespace StellarLauncher.App.ViewModels.Shell;

public enum ShellPage { Dashboard, Workspace, AddClient, LauncherSettings }

/// <summary>Page factories, injected so the shell never references page view-model types directly.</summary>
public sealed record ShellPages(
    Func<ShellViewModel, object> Dashboard,
    Func<ShellViewModel, ClientProfile, object> Workspace,
    Func<ShellViewModel, object> AddClient,
    Func<ShellViewModel, object> LauncherSettings);

/// <summary>The rail + navigation. Owns the loaded config and the client list; pages get `this` back.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IConfigStore _store;
    private readonly ClientSessions _sessions;
    private readonly ShellPages _pages;

    public LauncherConfig Config { get; private set; } = new();
    public ObservableCollection<RailClientItem> Clients { get; } = new();
    public ClientSessions Sessions => _sessions;
    public string LauncherVersionLabel => AppInfo.LauncherVersionLabel;

    /// <summary>Set by the Dashboard (Task 4) so the rail's quick ▶ launches through the same path.</summary>
    public Func<ClientProfile, Task>? QuickLaunchHandler { get; set; }

    [ObservableProperty] private object? _current;
    [ObservableProperty] private ShellPage _page;
    [ObservableProperty] private RailClientItem? _selectedClient;
    [ObservableProperty] private int _candidateCount;
    [ObservableProperty] private bool _launcherUpdateAvailable;
    [ObservableProperty] private string _launcherUpdateText = "";

    public bool HasCandidates => CandidateCount > 0;
    partial void OnCandidateCountChanged(int value) => OnPropertyChanged(nameof(HasCandidates));

    public ShellViewModel(IConfigStore store, ClientSessions sessions, ShellPages pages)
    {
        _store = store; _sessions = sessions; _pages = pages;
        _sessions.SessionChanged += s => Clients.FirstOrDefault(i => i.Client.Id == s.ClientId)?.Refresh();
    }

    public void Start()
    {
        Reload();
        _sessions.Reattach(Config.Clients);
        if (Config.Clients.Count == 0) { ShowAddClient(); return; }
        if (Config.Launcher.StartOn == "lastClient" && Config.Launcher.LastSelectedClientId is { } id && Config.FindById(id) is not null)
            ShowClientById(id);
        else ShowDashboard();
    }

    public void Reload()
    {
        Config = _store.Load();
        var selectedId = SelectedClient?.Client.Id;
        Clients.Clear();
        foreach (var c in Config.Clients) Clients.Add(new RailClientItem(c, _sessions.For(c)));
        SelectedClient = Clients.FirstOrDefault(i => i.Client.Id == selectedId);
        if (SelectedClient is not null) SelectedClient.IsSelected = true;
    }

    public void SaveConfig() => _store.Save(Config);

    [RelayCommand]
    private void ShowDashboard()
    {
        Select(null); Page = ShellPage.Dashboard; Current = _pages.Dashboard(this);
    }

    [RelayCommand]
    private void ShowClient(RailClientItem item)
    {
        Select(item);
        Config.Launcher.LastSelectedClientId = item.Client.Id; SaveConfig();
        Page = ShellPage.Workspace; Current = _pages.Workspace(this, item.Client);
    }

    public void ShowClientById(string id)
    {
        if (Clients.FirstOrDefault(i => i.Client.Id == id) is { } item) ShowClient(item);
    }

    [RelayCommand]
    private void ShowAddClient() { Select(null); Page = ShellPage.AddClient; Current = _pages.AddClient(this); }

    [RelayCommand]
    private void ShowLauncherSettings() { Select(null); Page = ShellPage.LauncherSettings; Current = _pages.LauncherSettings(this); }

    [RelayCommand]
    private Task QuickLaunch(RailClientItem item) => QuickLaunchHandler?.Invoke(item.Client) ?? Task.CompletedTask;

    public void ClientAdded(ClientProfile c)
    {
        Config.Clients.Add(c); SaveConfig(); Reload(); ShowClientById(c.Id);
    }

    public void ClientRemoved(string id)
    {
        Config.Clients.RemoveAll(c => c.Id == id);
        if (Config.Launcher.LastSelectedClientId == id) Config.Launcher.LastSelectedClientId = null;
        _sessions.Forget(id); SaveConfig(); Reload(); ShowDashboard();
    }

    /// <summary>Name / accent / channel changed on the selected client: rebuild rows, keep selection.</summary>
    public void ClientEdited() { SaveConfig(); Reload(); }

    private void Select(RailClientItem? item)
    {
        foreach (var i in Clients) i.IsSelected = ReferenceEquals(i, item);
        SelectedClient = item;
    }
}

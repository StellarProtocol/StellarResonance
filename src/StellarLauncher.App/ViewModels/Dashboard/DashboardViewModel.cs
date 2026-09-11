using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels.Shell;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Launch;
using StellarLauncher.Core.Matrix;
using StellarLauncher.Core.Model;

namespace StellarLauncher.App.ViewModels.Dashboard;

public sealed record DashboardServices(ClientInventory Inventory, RegistryCache Registry, FrameworkManifests Manifests,
    IPreLaunchReview Review, PluginInstallDeps Install, ClientCandidates Candidates);

/// <summary>All clients at once: launch tiles + the plugin matrix (mockup #dashboard).</summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private readonly DashboardServices _svc;
    private IReadOnlyList<ClientColumn> _columns = Array.Empty<ClientColumn>();
    private string? _latestStable;

    public ObservableCollection<ClientTileViewModel> Tiles { get; } = new();
    public ObservableCollection<MatrixColumnViewModel> Columns { get; } = new();
    public ObservableCollection<MatrixRowViewModel> Rows { get; } = new();

    [ObservableProperty] private string _summaryLine = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _showAllRows;

    public bool ShowMatrix => _shell.Config.Launcher.ShowMatrix;

    public DashboardViewModel(ShellViewModel shell, DashboardServices svc)
    {
        _shell = shell; _svc = svc;
        _shell.QuickLaunchHandler = LaunchClientAsync;
        _shell.Sessions.SessionChanged += _ => { foreach (var t in Tiles) t.Refresh(); UpdateSummary(); };
        foreach (var c in _shell.Config.Clients)
            Tiles.Add(new ClientTileViewModel(c, _shell.Sessions.For(c), LaunchClientAsync, x => _shell.Sessions.Stop(x), x => _shell.ShowClientById(x.Id)));
        _ = RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        _svc.Registry.Invalidate(); _svc.Manifests.Invalidate();
        try
        {
            var columns = new List<ClientColumn>();
            foreach (var tile in Tiles)
            {
                var registry = await _svc.Registry.ForChannelAsync(tile.Client.Channel, CancellationToken.None);
                var inv = _svc.Inventory.Read(tile.Client, registry);
                var latest = (await _svc.Manifests.LatestForAsync(tile.Client.Channel, CancellationToken.None))?.Latest;
                var col = new ClientColumn(tile.Client, inv, registry);
                columns.Add(col);
                tile.SetInventory(inv, latest, CountUpdates(col));
                _shell.Clients.FirstOrDefault(r => r.Client.Id == tile.Client.Id)?.SetFramework(inv.FrameworkVersion);
            }
            _columns = columns;
            _latestStable = (await _svc.Manifests.LatestForAsync("stable", CancellationToken.None))?.Latest;
            RebuildMatrix();
            _shell.CandidateCount = _svc.Candidates.Find(_shell.Config).Count;
            UpdateSummary();
            Status = "";
        }
        catch (Exception ex) { Status = $"refresh failed — {ex.Message}"; }
    }

    private static int CountUpdates(ClientColumn col) =>
        col.Registry.Count(e => PluginMatrixBuilder.Classify(col, e.Id).Kind == MatrixCellKind.UpdateAvailable);

    private void RebuildMatrix()
    {
        var matrix = PluginMatrixBuilder.Build(_columns);
        Columns.Clear(); foreach (var c in _columns) Columns.Add(new MatrixColumnViewModel(c));
        Rows.Clear();
        foreach (var row in ShowAllRows ? matrix.Rows : matrix.PresentRows)
            Rows.Add(new MatrixRowViewModel(row.PluginId, row.Name, row.Cells.Select((cell, i) => new MatrixCellViewModel(cell, CellAction(_columns[i], row.PluginId, cell)))));
    }

    partial void OnShowAllRowsChanged(bool value) => RebuildMatrix();

    private Func<Task>? CellAction(ClientColumn col, string pluginId, MatrixCell cell)
    {
        if (cell.Kind is not (MatrixCellKind.NotInstalled or MatrixCellKind.UpdateAvailable) || cell.TargetVersion is null) return null;
        var entry = col.Registry.First(e => e.Id == pluginId);
        var version = entry.Versions.First(v => v.Version == cell.TargetVersion);
        return async () =>
        {
            try { await PluginDownloads.InstallAsync(_svc.Install, col.Client.GameMiniDir, entry, version, s => Status = $"{col.Client.Name}: {entry.Name} — {s}"); }
            catch (Exception ex) { Status = $"{col.Client.Name}: {entry.Name} failed — {ex.Message}"; }
            await RefreshAsync();
        };
    }

    public async Task LaunchClientAsync(ClientProfile c)
    {
        await _shell.Sessions.LaunchAsync(c, _svc.Review, CancellationToken.None);
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var sessions = Tiles.Select(t => t.Session).ToList();
        var running = sessions.Count(s => s.State is SessionState.Running or SessionState.SteamHandoff);
        var preparing = sessions.Count(s => s.State is SessionState.Launching or SessionState.Preparing);
        var fw = _latestStable is null ? "framework manifest offline" : $"framework {_latestStable} is the latest stable";
        SummaryLine = $"{Tiles.Count} client{(Tiles.Count == 1 ? "" : "s")} · {running} running · {preparing} preparing · {fw} · refreshed {DateTime.Now:HH:mm:ss}";
    }

    [RelayCommand] private void AddClient() => _shell.ShowAddClientCommand.Execute(null);
    [RelayCommand] private void ToggleAllRows() => ShowAllRows = !ShowAllRows;
}

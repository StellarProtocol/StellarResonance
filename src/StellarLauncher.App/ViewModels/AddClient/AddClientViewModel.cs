using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.App.ViewModels.Shell;
using StellarLauncher.App.ViewModels.Workspace;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.ViewModels.AddClient;

/// <summary>Full-page Add client; also the first-run screen (mockup #add, spec § 5.7 / § 10).</summary>
public sealed partial class AddClientViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private readonly WorkspaceServices _svc;

    public ObservableCollection<CandidateRowViewModel> Rows { get; } = new();
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private int _hiddenCount;

    public bool IsFirstRun => _shell.Config.Clients.Count == 0;
    public bool IsWindows => _svc.Platform.IsWindows;
    public string AddLabel { get { var n = Rows.Count(r => r.Selected && r.IsAvailable); return n == 1 ? "Add 1 client" : $"Add {n} clients"; } }
    public bool CanAdd => Rows.Any(r => r.Selected && r.IsAvailable);
    public bool HasHidden => HiddenCount > 0;
    public string FoundLine => $"Detected installs — {Rows.Count} found";

    public AddClientViewModel(ShellViewModel shell, WorkspaceServices svc)
    {
        _shell = shell; _svc = svc;
        Refresh();
    }

    partial void OnHiddenCountChanged(int value) => OnPropertyChanged(nameof(HasHidden));

    [RelayCommand]
    public void Refresh()
    {
        Rows.Clear();
        var cfg = _shell.Config;
        var accents = new List<string>(cfg.Clients.Select(c => c.Accent));
        foreach (var c in _svc.Core.Candidates.Find(cfg))
        {
            var accent = AccentPalette.Next(accents); accents.Add(accent);
            Track(new CandidateRowViewModel(c, null, accent));
        }
        foreach (var path in _svc.Detector.Detect())
            if (_svc.Core.Candidates.ConfiguredFor(cfg, path) is { } existing)
                Track(new CandidateRowViewModel(new ClientCandidate(path, existing.Name, ClientNaming.DetectLayout(path), ClientNaming.IsWinePrefixPath(path), null, null), existing.Name, existing.Accent));
        HiddenCount = cfg.Launcher.DismissedDetections.Count;
        RaiseCounts();
    }

    private void Track(CandidateRowViewModel row)
    {
        row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CandidateRowViewModel.Selected)) RaiseCounts(); };
        Rows.Add(row);
    }

    private void RaiseCounts() { OnPropertyChanged(nameof(AddLabel)); OnPropertyChanged(nameof(CanAdd)); OnPropertyChanged(nameof(FoundLine)); }

    /// <summary>Browse result: a game_mini, a StarLauncher\game root, or a Steam flat folder.</summary>
    public void AddFromPath(string picked)
    {
        var path = _svc.Locator.FindGameMini(picked) ?? picked;
        if (_svc.Core.Candidates.ConfiguredFor(_shell.Config, path) is { } existing) { Status = $"{path} is already added as {existing.Name}"; return; }
        if (Rows.FirstOrDefault(r => ClientCandidates.SamePath(r.Path, path, IsWindows)) is { } dup) { dup.Selected = true; return; }
        var wine = IsWindows ? null : GameDetector.WinePrefixFor(path);
        var cand = new ClientCandidate(path, ClientNaming.ProposeName(path), ClientNaming.DetectLayout(path), wine is not null, wine, wine is not null ? _svc.Detector.DetectRunner() : null);
        // Accents already spoken for: every configured client AND every row on this page (a browsed row must not
        // repeat a detected row's colour).
        var inUse = _shell.Config.Clients.Select(c => c.Accent).Concat(Rows.Select(r => r.Accent));
        Track(new CandidateRowViewModel(cand, null, AccentPalette.Next(inUse)));
        Status = ""; RaiseCounts();
    }

    [RelayCommand]
    private void Add()
    {
        var cfg = _shell.Config;
        var added = new List<ClientProfile>();
        foreach (var row in Rows.Where(r => r.Selected && r.IsAvailable))
        {
            var profile = _svc.Core.Candidates.ToProfile(row.Candidate, cfg);
            profile.Name = UniqueName(row.Name.Trim().Length > 0 ? row.Name.Trim() : row.Candidate.ProposedName, cfg);
            cfg.Clients.Add(profile); added.Add(profile);
        }
        if (added.Count == 0) return;
        _shell.SaveConfig(); _shell.Reload();
        if (added.Count == 1) _shell.ShowClientById(added[0].Id); else _shell.ShowDashboardCommand.Execute(null);
    }

    private static string UniqueName(string proposed, LauncherConfig cfg)
    {
        var name = proposed;
        for (var n = 2; cfg.NameTaken(name); n++) name = $"{proposed} {n}";
        return name;
    }

    [RelayCommand] private void Skip() => _shell.ShowDashboardCommand.Execute(null);

    [RelayCommand]
    private void Hide(CandidateRowViewModel row)
    {
        _shell.Config.Launcher.DismissedDetections.Add(row.Path); _shell.SaveConfig();
        Rows.Remove(row); HiddenCount = _shell.Config.Launcher.DismissedDetections.Count; RaiseCounts();
        _shell.CandidateCount = _svc.Core.Candidates.Find(_shell.Config).Count;
    }

    [RelayCommand]
    private void ShowHidden()
    {
        _shell.Config.Launcher.DismissedDetections.Clear(); _shell.SaveConfig(); Refresh();
    }
}

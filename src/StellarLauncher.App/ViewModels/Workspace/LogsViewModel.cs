using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.App.Localization;
using StellarLauncher.App.Services;
using StellarLauncher.Core.Logs;

namespace StellarLauncher.App.ViewModels.Workspace;

public enum LogFilter { All, WarningsPlus, StellarOnly }

public sealed record LogLineViewModel(string Text, IBrush Brush, bool Highlight);

public sealed partial class CalloutViewModel : ObservableObject
{
    private readonly Func<Task> _fix;
    public LogCallout Callout { get; }
    public CalloutViewModel(LogCallout callout, Func<Task> fix) { Callout = callout; _fix = fix; }
    public CalloutKind Kind => Callout.Kind;
    public string Title => Callout.Title;
    public string Detail => Callout.Detail;
    public bool FixAvailable => Callout.FixAvailable;
    public string FixLabel => Callout.Kind switch
    {
        CalloutKind.DuplicatePluginSlot => Loc.T("logs.fix.collapse"),
        CalloutKind.FrameworkShadowCopy => Loc.T("logs.fix.evacuate"),
        CalloutKind.GamePatchedNewerRelease => Loc.T("logs.fix.repoint"),
        _ => Loc.T("logs.fix"),
    };
    [RelayCommand] private Task Fix() => _fix();
}

/// <summary>This client's log, tailed on a bounded timer only while the tab is visible (spec § 9).</summary>
public sealed partial class LogsViewModel : ObservableObject, IDisposable
{
    private const int TailLines = 400;
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(2);
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.Parse("#ffcf6b"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#ff9a9a"));
    private static readonly IBrush MessageBrush = new SolidColorBrush(Color.Parse("#9ec2ff"));
    private static readonly IBrush PlainBrush = new SolidColorBrush(Color.Parse("#aeb6d0"));
    private readonly ClientWorkspaceViewModel _ws;
    private IDisposable? _timer;
    private IReadOnlyList<LogLine> _raw = Array.Empty<LogLine>();

    public ObservableCollection<string> Files { get; } = new();
    public ObservableCollection<LogLineViewModel> Lines { get; } = new();
    public ObservableCollection<CalloutViewModel> Callouts { get; } = new();
    [ObservableProperty] private string? _selectedFile;
    [ObservableProperty] private LogFilter _filter = LogFilter.All;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _status = "";

    public string LogFolder => System.IO.Path.Combine(_ws.Client.GameMiniDir, "BepInEx");
    public bool IsBusyClient => _ws.Session.IsBusy;
    public bool IsAll => Filter == LogFilter.All; public bool IsWarnings => Filter == LogFilter.WarningsPlus; public bool IsStellar => Filter == LogFilter.StellarOnly;

    public LogsViewModel(ClientWorkspaceViewModel ws)
    {
        _ws = ws;
        _ws.Refreshed += RebuildCallouts;
        Files.Clear();
        foreach (var f in LogFiles.Candidates(_ws.Services.Fs, _ws.Client, _ws.Services.Platform.IsWindows)) Files.Add(f);
        SelectedFile = Files.FirstOrDefault();
        RebuildCallouts();
    }

    /// <summary>The view calls these on attach/detach; the timer exists ONLY while the tab is on screen (no unbounded polling).</summary>
    public void Activate() { _timer ??= _ws.Services.Timer(Poll, Reload); Reload(); }
    public void Deactivate() { _timer?.Dispose(); _timer = null; }

    /// <summary>Reached through the workspace's dispose cascade: a page navigated away from must not keep tailing.</summary>
    public void Dispose() { Deactivate(); _ws.Refreshed -= RebuildCallouts; }

    [RelayCommand]
    public void Reload()
    {
        _raw = SelectedFile is null ? Array.Empty<LogLine>() : LogTail.ReadLast(_ws.Services.Fs, SelectedFile, TailLines);
        ApplyFilter();
    }

    partial void OnSelectedFileChanged(string? value) => Reload();
    partial void OnSearchChanged(string value) => ApplyFilter();
    [RelayCommand] private void SetFilter(LogFilter f) { Filter = f; ApplyFilter(); }

    private void ApplyFilter()
    {
        var q = Search.Trim();
        Lines.Clear();
        foreach (var l in _raw.Where(Pass).Where(l => q.Length == 0 || l.Raw.Contains(q, StringComparison.OrdinalIgnoreCase)))
            Lines.Add(new LogLineViewModel(l.Raw, BrushFor(l.Level), l.Text.Contains("duplicate plugin id") || l.Text.Contains("newer version exists")));
        foreach (var p in new[] { nameof(IsAll), nameof(IsWarnings), nameof(IsStellar) }) OnPropertyChanged(p);
    }

    private bool Pass(LogLine l) => Filter switch
    {
        LogFilter.WarningsPlus => l.Level is LogLevel.Warning or LogLevel.Error or LogLevel.Fatal,
        LogFilter.StellarOnly => l.Source.Contains("Stellar", StringComparison.OrdinalIgnoreCase),
        _ => true,
    };

    private static IBrush BrushFor(LogLevel level) => level switch
    {
        LogLevel.Warning => WarnBrush, LogLevel.Error or LogLevel.Fatal => ErrorBrush, LogLevel.Message => MessageBrush, _ => PlainBrush,
    };

    public string LastLinesText(int n) => string.Join('\n', _raw.TakeLast(n).Select(l => l.Raw));

    private void RebuildCallouts()
    {
        Callouts.Clear();
        foreach (var c in _ws.Callouts) Callouts.Add(new CalloutViewModel(c, () => ApplyFixAsync(c)));
        OnPropertyChanged(nameof(IsBusyClient));
    }

    private async Task ApplyFixAsync(LogCallout c)
    {
        if (_ws.Session.IsBusy) { Status = Loc.T("logs.closeFirst"); return; }
        var fs = _ws.Services.Fs; var dir = _ws.Client.GameMiniDir; var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        try
        {
            switch (c.Kind)
            {
                case CalloutKind.DuplicatePluginSlot when c.Slot is { } slot:
                    var r = DuplicateSlotFix.Collapse(fs, dir, slot, stamp);
                    Status = Loc.TFormat("logs.collapsed", System.IO.Path.GetFileName(r.KeptDir), r.MovedDirs.Count, r.BackupDir)
                           + (r.FailedDirs.Count > 0 ? Loc.TFormat("logs.collapsed.failed", r.FailedDirs.Count) : ""); break;
                case CalloutKind.FrameworkShadowCopy:
                    var moved = ShadowCopyFix.Evacuate(fs, dir, stamp);
                    Status = Loc.Plural("logs.evacuated", moved.Count); break;
                case CalloutKind.GamePatchedNewerRelease when c.Path is { } p:
                    _ws.Client.GameMiniDir = p; _ws.SaveProfile(); Status = Loc.TFormat("logs.repointed", p); break;
            }
        }
        catch (Exception ex) { Status = Loc.TFormat("logs.fixFailed", ex.Message); }
        await _ws.RefreshAsync();
        Reload();
    }
}

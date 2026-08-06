using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.ViewModels;

public partial class PreLaunchReviewViewModel : ObservableObject
{
    private readonly PreLaunchPlan _plan;
    private readonly string _gameMini;
    private readonly VersionManifest? _frameworkTarget;   // when the framework row will be applied
    private readonly IInstaller _installer;
    private readonly IPluginInstaller _plugins;
    private readonly HttpClient _http;
    private readonly TaskCompletionSource<PreLaunchResult> _tcs = new();

    public Task<PreLaunchResult> Completion => _tcs.Task;
    public event Action? RequestClose;

    public ObservableCollection<PluginPlanRowViewModel> Rows { get; } = new();

    [ObservableProperty] private bool _hasFrameworkRow;
    [ObservableProperty] private bool _applyFramework;
    [ObservableProperty] private string _frameworkLabel = "";
    [ObservableProperty] private bool _frameworkBlockedByLauncher;
    [ObservableProperty] private bool _isApplying;
    [ObservableProperty] private string _title = "Before launching";
    [ObservableProperty] private string _statusLine = "";

    // The launch button is enabled only when every blocking plugin is resolved (updated or disabled).
    public bool CanLaunch => !IsApplying && Rows.All(r => r.IsResolved);
    // Auto-update ON hides the manual "Launch" secondary; OFF shows it.
    public bool ShowLaunchWithout { get; }

    public PreLaunchReviewViewModel(
        PreLaunchPlan plan, IReadOnlyList<PluginEntry> registry, VersionManifest? frameworkTarget,
        bool autoUpdate, string gameMini,
        IInstaller installer, IPluginInstaller plugins, HttpClient http)
    {
        _plan = plan; _frameworkTarget = frameworkTarget; _gameMini = gameMini;
        _installer = installer; _plugins = plugins; _http = http;
        ShowLaunchWithout = !autoUpdate;

        HasFrameworkRow = plan.FrameworkAction != FrameworkPlanAction.None;
        FrameworkBlockedByLauncher = plan.FrameworkAction == FrameworkPlanAction.UpdateBlockedByLauncher;
        FrameworkLabel = plan.FrameworkTarget is { } t ? $"Mod framework  v{InstalledOr("?")} → v{t}" : "";
        ApplyFramework = autoUpdate && plan.FrameworkAction == FrameworkPlanAction.UpdateAvailable;

        foreach (var item in plan.Items.Where(i => i.Status != PluginPlanStatus.UpToDate))
        {
            var entry = registry.FirstOrDefault(e => e.Id == item.Id);
            if (entry is null) continue;
            var target = item.TargetVersion is null ? null
                : entry.Versions.FirstOrDefault(v => v.Version == item.TargetVersion);
            var row = new PluginPlanRowViewModel(item, entry, target, preselect: autoUpdate);
            row.ResolvedChanged = () => OnPropertyChanged(nameof(CanLaunch));
            Rows.Add(row);
        }
        Title = autoUpdate ? "Updates are ready before you launch" : "Choose what to update";
    }

    private string InstalledOr(string fallback) => _plan.EffectiveFramework ?? fallback;

    [RelayCommand]
    private void Cancel() { _tcs.TrySetResult(PreLaunchResult.Cancel); RequestClose?.Invoke(); }

    // OFF-mode "Launch" (skip updates) — only offered when auto-update is off AND nothing blocks.
    [RelayCommand]
    private async Task Launch()
    {
        await ApplyDisablesAsync();
        _tcs.TrySetResult(PreLaunchResult.Proceed); RequestClose?.Invoke();
    }

    [RelayCommand]
    private async Task UpdateAllAndLaunch()
    {
        if (!CanLaunch) return;
        IsApplying = true; OnPropertyChanged(nameof(CanLaunch));
        try
        {
            if (ApplyFramework && _frameworkTarget is not null)
            {
                StatusLine = $"Updating framework to v{_frameworkTarget.Version}…";
                using var buf = new MemoryStream();
                await _http.DownloadToAsync(new Uri(_frameworkTarget.BundleUrl), buf, ProgressFor(p => StatusLine = DownloadStatus.Line("framework…", p)));
                buf.Position = 0;
                await _installer.InstallAsync(buf, _frameworkTarget.Sha256, _gameMini, _frameworkTarget.Version);
            }

            var selected = Rows.Where(r => r.IsSelected && r.Target is not null).ToList();
            for (int i = 0; i < selected.Count; i++)
            {
                var row = selected[i];
                row.StatusText = $"Updating… ({i + 1} of {selected.Count})";
                using var buf = new MemoryStream();
                var v = row.Target!;
                await _http.DownloadToAsync(new Uri(v.DllUrl), buf, ProgressFor(p => row.Percent = (p.Fraction ?? 0) * 100));
                buf.Position = 0;
                var dll = v.Dll ?? Path.GetFileName(new Uri(v.DllUrl).LocalPath);
                await _plugins.InstallAsync(buf, v.Sha256, _gameMini, row.Entry.Id, dll, v.Version);
                row.Percent = 100; row.StatusText = "Done";
            }

            await ApplyDisablesAsync();
            _tcs.TrySetResult(PreLaunchResult.Proceed); RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            StatusLine = $"update failed: {ex.Message}";
            IsApplying = false; OnPropertyChanged(nameof(CanLaunch));
        }
    }

    private Task ApplyDisablesAsync()
    {
        foreach (var row in Rows.Where(r => r.IsDisableChosen))
            _plugins.Disable(_gameMini, row.Entry.Id);
        return Task.CompletedTask;
    }

    private IProgress<DownloadProgress> ProgressFor(Action<DownloadProgress> sink)
        => new Progress<DownloadProgress>(sink);
}

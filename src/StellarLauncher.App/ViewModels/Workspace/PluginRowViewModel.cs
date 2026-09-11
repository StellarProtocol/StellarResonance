using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.Core.Matrix;

namespace StellarLauncher.App.ViewModels.Workspace;

public sealed record AlsoOnChip(string Text, bool IsPresent);

/// <summary>One plugin row on a client's Plugins tab (mockup #plugins).</summary>
public sealed partial class PluginRowViewModel : ObservableObject
{
    private static readonly string[][] Gradients =
    {
        new[] { "#7c5cff", "#37c8e0" }, new[] { "#ff6b6b", "#ffb347" }, new[] { "#2dd4bf", "#a3e635" },
        new[] { "#ff6ec7", "#7c5cff" }, new[] { "#37c8e0", "#54e3a0" }, new[] { "#ffb347", "#ff6ec7" }, new[] { "#5b8cff", "#2dd4bf" },
    };
    private readonly ClientPluginsViewModel _host;
    private bool _loading;

    public PluginItemViewModel Item { get; }
    public MatrixCell Cell { get; }
    public ObservableCollection<AlsoOnChip> AlsoOn { get; } = new();
    [ObservableProperty] private bool _enabled;

    public PluginRowViewModel(PluginItemViewModel item, MatrixCell cell, IEnumerable<AlsoOnChip> alsoOn, int index, ClientPluginsViewModel host)
    {
        Item = item; Cell = cell; _host = host;
        foreach (var c in alsoOn) AlsoOn.Add(c);
        _loading = true; Enabled = !item.IsDisabled && item.Installed; _loading = false;
        var g = Gradients[index % Gradients.Length];
        BadgeBrush = new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative), EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse(g[0]), 0), new GradientStop(Color.Parse(g[1]), 1) },
        };
    }

    public string Name => Item.Entry.Name;
    public string Author => Item.Entry.Author ?? "";
    public string Description => Item.Entry.Description;
    public string Initials => string.Concat(Item.Entry.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
    public IBrush BadgeBrush { get; }
    public bool IsInstalled => Item.Installed;
    public bool IsDisabled => Item.IsDisabled;
    public bool IsPresent => IsInstalled || IsDisabled;
    public bool HasUpdate => Cell.Kind == MatrixCellKind.UpdateAvailable;
    public string? TargetVersion => Cell.TargetVersion;
    public string VersionLine => Cell.Kind switch
    {
        MatrixCellKind.UpdateAvailable => $"{Cell.InstalledVersion} ↑ {Cell.TargetVersion}",
        MatrixCellKind.Installed => $"{Cell.InstalledVersion ?? "?"} ✓",
        MatrixCellKind.Incompatible => $"{Cell.InstalledVersion} ✗ needs another framework",
        MatrixCellKind.Disabled => $"{Cell.InstalledVersion ?? "?"} · disabled",
        MatrixCellKind.NotInstalled => $"{Cell.TargetVersion} · not installed",
        MatrixCellKind.NoCompatibleVersion => "no version for this framework",
        MatrixCellKind.NeedsFramework => "needs framework",
        _ => "—",
    };

    [RelayCommand] private Task Install() => _host.InstallVersionAsync(this, Cell.TargetVersion);
    [RelayCommand] private Task Update() => _host.InstallVersionAsync(this, Cell.TargetVersion);
    [RelayCommand] private void Open() => _host.OpenPluginCommand.Execute(Item);

    partial void OnEnabledChanged(bool value) { if (!_loading) _ = _host.SetEnabledAsync(this, value); }
}

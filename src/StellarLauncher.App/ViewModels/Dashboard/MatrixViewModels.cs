using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.App.Services;
using StellarLauncher.Core.Matrix;

namespace StellarLauncher.App.ViewModels.Dashboard;

public sealed class MatrixColumnViewModel
{
    public MatrixColumnViewModel(ClientColumn col)
    {
        Name = col.Client.Name;
        Subtitle = col.Inventory.FrameworkVersion is null ? "no framework" : $"fw {col.Inventory.FrameworkVersion} · {col.Client.Channel}";
        AccentBrush = AccentBrushes.Solid(col.Client.Accent);
    }
    public string Name { get; }
    public string Subtitle { get; }
    public IBrush AccentBrush { get; }
}

/// <summary>One matrix cell: label + colours by kind; clickable when it offers install/update (spec § 5.2).</summary>
public sealed partial class MatrixCellViewModel
{
    private readonly Func<Task>? _action;

    public MatrixCellViewModel(MatrixCell cell, Func<Task>? action)
    {
        Kind = cell.Kind; _action = action;
        (Label, Foreground, Background, IsDashed) = cell.Kind switch
        {
            MatrixCellKind.Installed => ($"{cell.InstalledVersion ?? "?"} ✓", "#9affd0", "#1F54e3a0", false),
            MatrixCellKind.UpdateAvailable => ($"{cell.InstalledVersion} ↑ {cell.TargetVersion}", "#ffcf6b", "#24ffcf6b", false),
            MatrixCellKind.Incompatible => ($"{cell.InstalledVersion} ✗ incompatible", "#ff9a9a", "#1Fff6b6b", false),
            MatrixCellKind.Disabled => ($"{cell.InstalledVersion ?? "?"} · disabled", "#ff9a9a", "#1Fff6b6b", false),
            MatrixCellKind.NotInstalled => ("＋ install", "#b7a8ff", "#00000000", true),
            // Muted, but still legible on the dark ground (owner 2026-09-11: the matrix text was hard to read).
            MatrixCellKind.NoCompatibleVersion => ("no compatible version", "#8a93b2", "#00000000", false),
            MatrixCellKind.NeedsFramework => ("needs framework", "#8a93b2", "#00000000", false),
            _ => ("—", "#8a93b2", "#00000000", false),
        };
    }

    public MatrixCellKind Kind { get; }
    public string Label { get; }
    public IBrush ForegroundBrush => new SolidColorBrush(Color.Parse(Foreground));
    public IBrush BackgroundBrush => new SolidColorBrush(Color.Parse(Background));
    public IBrush BorderBrush => IsDashed ? new SolidColorBrush(Color.Parse("#737c5cff")) : Brushes.Transparent;
    public string Foreground { get; }
    public string Background { get; }
    public bool IsDashed { get; }
    public bool IsClickable => _action is not null;

    [RelayCommand] private Task Click() => _action?.Invoke() ?? Task.CompletedTask;
}

public sealed class MatrixRowViewModel
{
    public MatrixRowViewModel(string pluginId, string name, IEnumerable<MatrixCellViewModel> cells)
    {
        PluginId = pluginId; Name = name; Cells = new ObservableCollection<MatrixCellViewModel>(cells);
    }
    public string PluginId { get; }
    public string Name { get; }
    public ObservableCollection<MatrixCellViewModel> Cells { get; }
}

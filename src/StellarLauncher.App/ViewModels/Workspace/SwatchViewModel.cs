using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.App.Services;

namespace StellarLauncher.App.ViewModels.Workspace;

/// <summary>One accent colour chip on the Settings tab.</summary>
public sealed partial class SwatchViewModel : ObservableObject
{
    public string Hex { get; }
    public IBrush Brush { get; }
    [ObservableProperty] private bool _isSelected;
    public IRelayCommand PickCommand { get; }

    public SwatchViewModel(string hex, bool selected, Action<string> pick)
    {
        Hex = hex; Brush = AccentBrushes.Solid(hex); _isSelected = selected;
        PickCommand = new RelayCommand(() => pick(hex));
    }
}

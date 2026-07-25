using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using StellarLauncher.App.ViewModels;

namespace StellarLauncher.App.Views;

public partial class PluginsView : UserControl
{
    public PluginsView() => AvaloniaXamlLoader.Load(this);

    // Whole-card click opens the plugin detail page — except when the press landed on one of the
    // card's interactive children (install buttons, version combo, its popup items, …).
    private void OnCardPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (sender is not Border { DataContext: PluginItemViewModel item } card) return;
        for (var node = e.Source as Avalonia.Visual; node is not null && node != card; node = node.GetVisualParent())
            if (node is Button or ComboBox or ComboBoxItem or TextBox or ToggleSwitch or ScrollBar) return;
        if (DataContext is PluginsViewModel vm) vm.OpenPluginCommand.Execute(item);
    }

    // Click anywhere on the dimmed backdrop (or the image) closes the lightbox.
    private void OnLightboxPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (DataContext is PluginsViewModel vm) vm.CloseLightboxCommand.Execute(null);
    }
}

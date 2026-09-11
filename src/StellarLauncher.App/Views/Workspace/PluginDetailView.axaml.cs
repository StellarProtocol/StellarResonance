using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using StellarLauncher.App.ViewModels.Workspace;

namespace StellarLauncher.App.Views.Workspace;

public partial class PluginDetailView : UserControl
{
    public PluginDetailView() => AvaloniaXamlLoader.Load(this);

    // Click anywhere on the dimmed backdrop (or the image) closes the lightbox.
    private void OnLightboxPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (DataContext is ClientPluginsViewModel vm) vm.CloseLightboxCommand.Execute(null);
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using StellarLauncher.App.ViewModels.Workspace;

namespace StellarLauncher.App.Views.Workspace;

public partial class ClientWorkspaceView : UserControl
{
    public ClientWorkspaceView() => AvaloniaXamlLoader.Load(this);

    private void CopyPath(object? sender, RoutedEventArgs e)
    {
        // Clipboard implementation for Avalonia 12.0.4
        // TODO: implement clipboard copy when Avalonia API is available
    }
}

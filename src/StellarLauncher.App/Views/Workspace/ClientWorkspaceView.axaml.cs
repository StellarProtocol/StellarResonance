using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using StellarLauncher.App.ViewModels.Workspace;

namespace StellarLauncher.App.Views.Workspace;

public partial class ClientWorkspaceView : UserControl
{
    public ClientWorkspaceView() => AvaloniaXamlLoader.Load(this);

    private async void CopyPath(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is ClientWorkspaceViewModel vm && TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                await cb.SetTextAsync(vm.Path);
        }
        catch (Exception) { /* clipboard unavailable (headless/Wayland quirk) — copying is best-effort */ }
    }
}

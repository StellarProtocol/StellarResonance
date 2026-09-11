using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using StellarLauncher.App.ViewModels.AddClient;

namespace StellarLauncher.App.Views.AddClient;

public partial class AddClientView : UserControl
{
    public AddClientView() => AvaloniaXamlLoader.Load(this);

    // async void event handler ⇒ guarded.
    private async void Browse(object? sender, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) return;
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select a game_mini folder, a StarLauncher\\game folder, or a Steam \"Blue Protocol Star Resonance\" folder",
                AllowMultiple = false,
            });
            if (folders.Count > 0) (DataContext as AddClientViewModel)?.AddFromPath(folders[0].Path.LocalPath);
        }
        catch (Exception) { /* picker unavailable — detected rows still work */ }
    }
}

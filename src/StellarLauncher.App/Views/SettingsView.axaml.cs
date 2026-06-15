using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using StellarLauncher.App.ViewModels;

namespace StellarLauncher.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => AvaloniaXamlLoader.Load(this);

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    private async void BrowseGame(object? sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync("Select the game_mini folder (or the StarLauncher\\game folder)");
        if (folder is not null) Vm?.SetGameFromPicked(folder);
    }

    private async void BrowsePrefix(object? sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync("Select the WINEPREFIX folder");
        if (folder is not null && Vm is not null) Vm.WinePrefix = folder;
    }

    private async void BrowseRunner(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select the Proton / Wine executable",
            AllowMultiple = false,
        });
        if (files.Count > 0 && Vm is not null) Vm.Runner = files[0].Path.LocalPath;
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }
}

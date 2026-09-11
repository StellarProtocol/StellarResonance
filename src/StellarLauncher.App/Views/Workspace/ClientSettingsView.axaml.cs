using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using StellarLauncher.App.ViewModels.Workspace;

namespace StellarLauncher.App.Views.Workspace;

public partial class ClientSettingsView : UserControl
{
    public ClientSettingsView() => AvaloniaXamlLoader.Load(this);

    private ClientSettingsViewModel? Vm => DataContext as ClientSettingsViewModel;

    // Every handler is async void ⇒ each body is guarded (an unobserved exception here would crash the app).
    private async void BrowseGame(object? sender, RoutedEventArgs e)
    {
        try { var folder = await PickFolderAsync("Select the game_mini folder (or the StarLauncher\\game folder)"); if (folder is not null) Vm?.SetGameFromPicked(folder); }
        catch (Exception) { /* picker unavailable — the text box still accepts a typed path */ }
    }

    private async void BrowsePrefix(object? sender, RoutedEventArgs e)
    {
        try { var folder = await PickFolderAsync("Select the WINEPREFIX folder"); if (folder is not null && Vm is not null) Vm.WinePrefix = folder; }
        catch (Exception) { }
    }

    private async void BrowseRunner(object? sender, RoutedEventArgs e)
    {
        try { var path = await PickFileAsync("Select the Proton / Wine executable"); if (path is not null && Vm is not null) Vm.Runner = path; }
        catch (Exception) { }
    }

    private async void BrowsePre(object? sender, RoutedEventArgs e)
    {
        try { var path = await PickFileAsync("Select the pre-launch script"); if (path is not null && Vm is not null) Vm.PreLaunch = path; }
        catch (Exception) { }
    }

    private async void BrowsePost(object? sender, RoutedEventArgs e)
    {
        try { var path = await PickFileAsync("Select the post-exit script"); if (path is not null && Vm is not null) Vm.PostExit = path; }
        catch (Exception) { }
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    private async Task<string?> PickFileAsync(string title)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }
}

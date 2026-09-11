using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using StellarLauncher.App.ViewModels;

namespace StellarLauncher.App.Views;

public partial class LauncherSettingsView : UserControl
{
    public LauncherSettingsView() => AvaloniaXamlLoader.Load(this);
    private LauncherSettingsViewModel? Vm => DataContext as LauncherSettingsViewModel;

    // Every handler is async void ⇒ each body is guarded (an unobserved exception here would crash the app).
    private async void ExportClicked(object? s, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null || Vm is null) return;
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export clients", SuggestedFileName = "stellar-clients.json" });
            if (file is not null) Vm.Export(file.Path.LocalPath);
        }
        catch (Exception) { /* picker unavailable */ }
    }

    private async void ImportClicked(object? s, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null || Vm is null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import clients", AllowMultiple = false });
            if (files.Count > 0) Vm.Import(files[0].Path.LocalPath);
        }
        catch (Exception) { /* picker unavailable */ }
    }

    private void OpenFolder(object? s, RoutedEventArgs e)
    {
        if (Vm is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.Path.GetDirectoryName(Vm.SettingsPath)!) { UseShellExecute = true }); }
        catch (Exception) { /* no file manager registered */ }
    }
}

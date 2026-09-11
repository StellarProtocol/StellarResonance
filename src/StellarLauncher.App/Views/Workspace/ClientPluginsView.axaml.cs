using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StellarLauncher.App.ViewModels.Workspace;
using StellarLauncher.Core.Clients;

namespace StellarLauncher.App.Views.Workspace;

public partial class ClientPluginsView : UserControl
{
    public ClientPluginsView() => AvaloniaXamlLoader.Load(this);

    // async void event handler ⇒ the body is guarded; the command itself reports failures on Status.
    private async void OnCopySourcePicked(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (sender is not ComboBox cb || cb.SelectedItem is not ClientProfile source || DataContext is not ClientPluginsViewModel vm) return;
            cb.SelectedItem = null;   // it is an action picker, not a selection
            await vm.CopySetFromCommand.ExecuteAsync(source);
        }
        catch (Exception) { /* surfaced on the tab's Status line by the command; never let it escape the handler */ }
    }
}

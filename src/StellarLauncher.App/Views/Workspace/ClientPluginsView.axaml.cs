using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StellarLauncher.App.ViewModels.Workspace;
using StellarLauncher.Core.Clients;

namespace StellarLauncher.App.Views.Workspace;

public partial class ClientPluginsView : UserControl
{
    private ClientPluginsViewModel? _subscribed;

    public ClientPluginsView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
    }

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

    // 2.1.2: the plugin list row's extras pill opens the detail page already scrolled to DEPENDENCIES — the
    // VM signals readiness (it has already awaited the section's own data refresh); this still defers one
    // layout pass (DispatcherPriority.Loaded) so the now-visible detail view has actually been measured
    // before we go looking for its ScrollViewer/header.
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribed is not null) _subscribed.ScrollToDependenciesRequested -= OnScrollToDependenciesRequested;
        _subscribed = DataContext as ClientPluginsViewModel;
        if (_subscribed is not null) _subscribed.ScrollToDependenciesRequested += OnScrollToDependenciesRequested;
    }

    private void OnScrollToDependenciesRequested() =>
        Dispatcher.UIThread.Post(ScrollDetailToDependencies, DispatcherPriority.Loaded);

    private void ScrollDetailToDependencies()
    {
        var detail = this.GetVisualDescendants().OfType<PluginDetailView>().FirstOrDefault();
        if (detail is null) return;
        var scroller = detail.GetVisualDescendants().OfType<ScrollViewer>().OrderByDescending(s => s.Extent.Height).FirstOrDefault();
        var header = detail.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == "DEPENDENCIES");
        if (scroller is null || header is null || scroller.Content is not Visual content) return;
        var y = header.TranslatePoint(new Point(0, 0), content)?.Y ?? 0;
        scroller.Offset = new Vector(0, Math.Max(0, y - 20));
    }
}

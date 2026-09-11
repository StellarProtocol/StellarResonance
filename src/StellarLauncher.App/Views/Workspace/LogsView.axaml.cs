using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using StellarLauncher.App.ViewModels.Workspace;

namespace StellarLauncher.App.Views.Workspace;

public partial class LogsView : UserControl
{
    public LogsView() => AvaloniaXamlLoader.Load(this);
    private LogsViewModel? Vm => DataContext as LogsViewModel;

    // The tail timer lives only while this view is on screen.
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Vm?.Activate(); }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e) { Vm?.Deactivate(); base.OnDetachedFromVisualTree(e); }

    // `SetTextAsync` is an extension (Avalonia.Input.Platform.ClipboardExtensions); async void ⇒ guarded.
    private async void CopyTail(object? s, RoutedEventArgs e)
    {
        try
        {
            if (Vm is null || TopLevel.GetTopLevel(this)?.Clipboard is not { } cb) return;
            await cb.SetTextAsync(Vm.LastLinesText(200));
        }
        catch (Exception) { /* clipboard unavailable — copying is best-effort */ }
    }

    private void OpenFolder(object? s, RoutedEventArgs e)
    {
        if (Vm is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Vm.LogFolder) { UseShellExecute = true }); }
        catch (Exception) { /* no file manager registered */ }
    }
}

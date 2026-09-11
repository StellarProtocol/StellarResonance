using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using StellarLauncher.App.Services;

namespace StellarLauncher.App.Views;

/// <summary>Modal yes/no dialog. The instance handed to the composition root is a factory: each question opens a fresh window.</summary>
public partial class ConfirmDialog : Window, IConfirm
{
    private readonly Func<Window?> _owner;
    public ConfirmDialog() : this(() => null) { }
    public ConfirmDialog(Func<Window?> owner) { _owner = owner; AvaloniaXamlLoader.Load(this); }

    public async Task<bool> AskAsync(string title, string body, string okLabel)
    {
        var dlg = new ConfirmDialog(_owner);
        dlg.FindControl<TextBlock>("TitleText")!.Text = title;
        dlg.FindControl<TextBlock>("BodyText")!.Text = body;
        dlg.FindControl<Button>("OkButton")!.Content = okLabel;
        var owner = _owner();
        return owner is not null && await dlg.ShowDialog<bool>(owner);
    }

    private void OnOk(object? s, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? s, RoutedEventArgs e) => Close(false);
}

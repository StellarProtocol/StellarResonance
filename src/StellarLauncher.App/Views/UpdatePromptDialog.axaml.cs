using Avalonia.Controls;
using Avalonia.Interactivity;
using StellarLauncher.App.ViewModels;

namespace StellarLauncher.App.Views;

public partial class UpdatePromptDialog : Window
{
    public UpdatePromptDialog(string current, string latest)
    {
        InitializeComponent();
        CurrentVersion.Text = $"v{current}";
        LatestVersion.Text  = $"v{latest}";
        LaunchAnywayButton.Content = $"Launch v{current}";
    }

    private void OnLaunchAnyway(object? sender, RoutedEventArgs e)    => Close(UpdateLaunchChoice.LaunchAnyway);
    private void OnUpdateAndLaunch(object? sender, RoutedEventArgs e) => Close(UpdateLaunchChoice.UpdateAndLaunch);
    private void OnCancel(object? sender, RoutedEventArgs e)          => Close(UpdateLaunchChoice.Cancel);
}

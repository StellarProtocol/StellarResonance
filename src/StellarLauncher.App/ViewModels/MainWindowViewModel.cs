using CommunityToolkit.Mvvm.ComponentModel;

namespace StellarLauncher.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public HomeViewModel Home { get; }
    public SettingsViewModel Settings { get; }
    public PluginsViewModel Plugins { get; }

    [ObservableProperty] private object _current;

    public MainWindowViewModel(HomeViewModel home, SettingsViewModel settings, PluginsViewModel plugins)
    {
        Home = home;
        Settings = settings;
        Plugins = plugins;
        _current = home;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ShowHome()
    {
        Current = Home;
        Home.RefreshCommand.Execute(null);   // pick up game-path/settings edits made in Settings
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand] private void ShowSettings() => Current = Settings;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ShowPlugins()
    {
        Current = Plugins;
        Plugins.ReloadCommand.Execute(null);
    }
}

using CommunityToolkit.Mvvm.ComponentModel;

namespace StellarLauncher.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public HomeViewModel Home { get; }
    public SettingsViewModel Settings { get; }
    public PluginsViewModel Plugins { get; }

    [ObservableProperty] private object _current;

    private bool _guidedToSetup;   // guide to Settings at most once per session

    public MainWindowViewModel(HomeViewModel home, SettingsViewModel settings, PluginsViewModel plugins)
    {
        Home = home;
        Settings = settings;
        Plugins = plugins;
        _current = home;

        // First-run guidance: if the launcher can't find the game, send the user straight to Settings
        // to set the path. Home's detect runs synchronously in its ctor (before this subscription), so
        // check the current value now AND subscribe for any later flip.
        Home.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HomeViewModel.NeedsGameSetup)) GuideToSetupIfNeeded();
        };
        GuideToSetupIfNeeded();
    }

    private void GuideToSetupIfNeeded()
    {
        if (_guidedToSetup || !Home.NeedsGameSetup) return;
        _guidedToSetup = true;
        Current = Settings;
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

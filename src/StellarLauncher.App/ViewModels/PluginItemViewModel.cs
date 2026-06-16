using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.ViewModels;

public partial class PluginItemViewModel : ObservableObject
{
    private readonly PluginsViewModel _parent;
    private readonly string? _framework;     // installed framework version (null = not installed)
    public PluginEntry Entry { get; }

    [ObservableProperty] private bool _installed;
    [ObservableProperty] private string? _installedVersion;
    [ObservableProperty] private string _action = "";
    [ObservableProperty] private PluginVersion? _selectedVersion;   // bound to the per-row ComboBox
    [ObservableProperty] private bool _compatible;
    [ObservableProperty] private string _compatNote = "";
    [ObservableProperty] private string _installLabel = "Install";
    [ObservableProperty] private bool _confirmVisible;
    [ObservableProperty] private bool _isDowngrade;

    public ObservableCollection<PluginVersion> Versions { get; } = new();

    public PluginItemViewModel(PluginEntry entry, string? installedVersion, string? installedFramework, PluginsViewModel parent)
    {
        Entry = entry; _parent = parent; _framework = installedFramework;
        _installedVersion = installedVersion;
        _installed = installedVersion is not null;
        foreach (var v in entry.Versions) Versions.Add(v);
        SelectedVersion = Versions.FirstOrDefault();   // newest first
    }

    public string Name => Entry.Name;
    public string Description => Entry.Description;
    public string Author => Entry.Author ?? "";

    // Selected version's changelog (may be null); the view guards visibility.
    public Changelog? SelectedChangelog => SelectedVersion?.Changelog;

    partial void OnSelectedVersionChanged(PluginVersion? value)
    {
        ConfirmVisible = false;
        IsDowngrade = false;
        OnPropertyChanged(nameof(SelectedChangelog));

        if (value is null) { Compatible = false; CompatNote = ""; InstallLabel = "Install"; return; }

        if (_framework is null)
        {
            Compatible = false; CompatNote = "install the framework first"; InstallLabel = "Install";
            return;
        }

        Compatible = VersionService.IsModSystemCompatible(_framework, value.MinModSystemVersion, value.MaxModSystemVersion);
        if (!Compatible)
        {
            CompatNote = VersionService.IsNewer(value.MinModSystemVersion, _framework)
                ? $"requires StellarResonance ≥ {value.MinModSystemVersion}"
                : $"needs StellarResonance ≤ {value.MaxModSystemVersion}";
            InstallLabel = "Incompatible";
            return;
        }

        CompatNote = "";
        if (_installedVersion is null)
            InstallLabel = $"Install v{value.Version}";
        else if (VersionService.IsNewer(value.Version, _installedVersion))
            InstallLabel = $"Update to v{value.Version}";
        else if (VersionService.IsNewer(_installedVersion, value.Version))
            { InstallLabel = $"Downgrade to v{value.Version}"; IsDowngrade = true; }
        else
            InstallLabel = $"Reinstall v{value.Version}";
    }

    // Called by the parent after a successful install to refresh installed state + label.
    public void MarkInstalled(string version)
    {
        InstalledVersion = version;
        Installed = true;
        OnSelectedVersionChanged(SelectedVersion);   // recompute the label against the new installed version
    }

    public void MarkRemoved()
    {
        InstalledVersion = null;
        Installed = false;
        OnSelectedVersionChanged(SelectedVersion);
    }

    [RelayCommand] private void RequestInstall() { if (Compatible) ConfirmVisible = true; }
    [RelayCommand] private void CancelInstall() => ConfirmVisible = false;
    [RelayCommand] private Task ConfirmInstall() { ConfirmVisible = false; return _parent.InstallAsync(this); }
    [RelayCommand] private Task Remove() => _parent.RemoveAsync(this);
}

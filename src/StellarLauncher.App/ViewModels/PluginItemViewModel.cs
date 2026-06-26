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
    [ObservableProperty] private bool _isUpdate;
    [ObservableProperty] private bool _isReinstall;
    [ObservableProperty] private bool _isPlainInstall;

    public ObservableCollection<PluginVersion> Versions { get; } = new();

    public PluginItemViewModel(PluginEntry entry, bool installed, string? installedVersion,
                               string? installedFramework, PluginsViewModel parent)
    {
        Entry = entry; _parent = parent; _framework = installedFramework;
        _installed = installed;                 // may be true with a null version (adopted, unmanaged install)
        _installedVersion = installedVersion;
        foreach (var v in entry.Versions) Versions.Add(v);
        SelectedVersion = Versions.FirstOrDefault();   // newest first
    }

    public string Name => Entry.Name;
    public string Description => Entry.Description;
    public string Author => Entry.Author ?? "";

    // Selected version's changelog (may be null); the view guards visibility.
    public Changelog? SelectedChangelog => SelectedVersion?.Changelog;

    // Canonical on-disk DLL filename for the selected version (for install/detect/remove).
    public string? CanonicalDll => SelectedVersion is { } v
        ? (v.Dll ?? System.IO.Path.GetFileName(new System.Uri(v.DllUrl).LocalPath))
        : null;

    public string InstalledBadge => InstalledVersion is { } iv ? $"INSTALLED v{iv}" : "INSTALLED";

    // True when the newest registry version is strictly newer than what's installed.
    public bool HasUpdate => Installed && InstalledVersion is { } iv
        && Versions.Count > 0
        && VersionService.IsNewer(Versions[0].Version, iv);

    partial void OnInstalledChanged(bool value)
    {
        OnPropertyChanged(nameof(InstalledBadge));
        OnPropertyChanged(nameof(HasUpdate));
    }
    partial void OnInstalledVersionChanged(string? value)
    {
        OnPropertyChanged(nameof(InstalledBadge));
        OnPropertyChanged(nameof(HasUpdate));
    }

    partial void OnSelectedVersionChanged(PluginVersion? value)
    {
        ConfirmVisible = false;
        IsDowngrade = false;
        OnPropertyChanged(nameof(SelectedChangelog));

        if (value is null) { Compatible = false; CompatNote = ""; InstallLabel = "Install"; IsUpdate = false; IsReinstall = false; IsPlainInstall = false; return; }

        if (_framework is null)
        {
            Compatible = false; CompatNote = "install the framework first"; InstallLabel = "Install"; IsUpdate = false; IsReinstall = false; IsPlainInstall = false;
            return;
        }

        Compatible = VersionService.IsModSystemCompatible(_framework, value.MinModSystemVersion, value.MaxModSystemVersion);
        if (!Compatible)
        {
            CompatNote = VersionService.IsNewer(value.MinModSystemVersion, _framework)
                ? $"requires StellarResonance ≥ {value.MinModSystemVersion}"
                : $"needs StellarResonance ≤ {value.MaxModSystemVersion}";
            InstallLabel = "Incompatible"; IsUpdate = false; IsReinstall = false; IsPlainInstall = false;
            return;
        }

        CompatNote = "";
        if (!Installed)
            { InstallLabel = $"Install v{value.Version}"; IsUpdate = false; IsReinstall = false; IsPlainInstall = true; }
        else if (InstalledVersion is null)
            { InstallLabel = $"Reinstall v{value.Version}"; IsUpdate = false; IsReinstall = true; IsPlainInstall = false; }   // present but unmanaged (no version marker) → adopt
        else if (VersionService.IsNewer(value.Version, InstalledVersion))
            { InstallLabel = $"Update to v{value.Version}"; IsUpdate = true; IsReinstall = false; IsPlainInstall = false; }
        else if (VersionService.IsNewer(InstalledVersion, value.Version))
            { InstallLabel = $"Downgrade to v{value.Version}"; IsDowngrade = true; IsUpdate = false; IsReinstall = false; IsPlainInstall = false; }
        else
            { InstallLabel = $"Reinstall v{value.Version}"; IsUpdate = false; IsReinstall = true; IsPlainInstall = false; }
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

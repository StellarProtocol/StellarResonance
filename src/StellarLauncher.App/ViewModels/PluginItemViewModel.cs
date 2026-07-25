using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
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

    // ---- detail page data (media gallery, guide, links) — loaded lazily on first open ----

    public IReadOnlyList<string> Tags => Entry.Tags ?? Array.Empty<string>();
    public bool HasTags => Tags.Count > 0;
    public string? Homepage => Entry.Homepage;
    public bool HasHomepage => !string.IsNullOrWhiteSpace(Entry.Homepage);
    // Provenance link from the newest version; ".git" stripped so it opens as a web page.
    public string? SourceUrl => Versions.FirstOrDefault()?.SourceRepository is { } r
        ? (r.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? r[..^4] : r)
        : null;
    public bool HasSource => SourceUrl is not null;

    public ObservableCollection<MediaItemViewModel> Media { get; } = new();
    public bool HasMedia => Media.Count > 0;
    public bool HasGuideUrl => Entry.GuideUrl is not null;
    [ObservableProperty] private string? _guideMarkdown;
    [ObservableProperty] private string _guideStatus = "";
    public bool HasGuideStatus => GuideStatus.Length > 0;
    partial void OnGuideStatusChanged(string value) => OnPropertyChanged(nameof(HasGuideStatus));

    private bool _detailLoaded;

    // Populates the media tiles and fetches the guide markdown the first time the detail page
    // opens. Runs on the UI thread; downloads are awaited so property sets stay on the UI thread.
    public async Task EnsureDetailLoadedAsync(HttpClient http, Action<Bitmap> openLightbox)
    {
        if (_detailLoaded) return;
        _detailLoaded = true;
        if (Entry.Media is { Count: > 0 } media)
        {
            foreach (var m in media)
                if (!string.IsNullOrWhiteSpace(m?.Url)) Media.Add(new MediaItemViewModel(m!, http, openLightbox));
            OnPropertyChanged(nameof(HasMedia));
            foreach (var tile in Media) _ = tile.LoadAsync();
        }
        if (Entry.GuideUrl is { } guideUrl)
        {
            GuideStatus = "loading guide…";
            try
            {
                GuideMarkdown = await http.GetStringAsync(guideUrl);
                GuideStatus = "";
            }
            catch { GuideStatus = "guide unavailable (couldn't download it — check your connection)"; }
        }
    }

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

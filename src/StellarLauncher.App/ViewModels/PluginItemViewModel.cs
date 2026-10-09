using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.App.Localization;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.ViewModels;

public partial class PluginItemViewModel : ObservableObject
{
    private readonly IPluginActions _parent;
    private readonly string? _framework;     // installed framework version (null = not installed)
    public PluginEntry Entry { get; }

    [ObservableProperty] private bool _installed;
    [ObservableProperty] private string? _installedVersion;
    [ObservableProperty] private string _action = "";
    [ObservableProperty] private PluginVersion? _selectedVersion;   // bound to the per-row ComboBox
    [ObservableProperty] private bool _compatible;
    [ObservableProperty] private string _compatNote = "";
    [ObservableProperty] private string _installLabel = Loc.T("common.install");
    [ObservableProperty] private bool _confirmVisible;
    [ObservableProperty] private bool _isDowngrade;
    [ObservableProperty] private bool _isUpdate;
    [ObservableProperty] private bool _isReinstall;
    [ObservableProperty] private bool _isPlainInstall;
    [ObservableProperty] private bool _isDisabled;

    public ObservableCollection<PluginVersion> Versions { get; } = new();

    public PluginItemViewModel(PluginEntry entry, bool installed, string? installedVersion,
                               string? installedFramework, IPluginActions parent)
    {
        Entry = entry; _parent = parent; _framework = installedFramework;
        _installed = installed;                 // may be true with a null version (adopted, unmanaged install)
        _installedVersion = installedVersion;
        foreach (var v in entry.Versions) Versions.Add(v);
        SelectedVersion = Versions.FirstOrDefault();   // newest first
        // Labels/notes are rendered at selection time: re-render them in the new language, keeping an open confirm.
        Loc.Subscribe(this, vm =>
        {
            var confirm = vm.ConfirmVisible;
            vm.OnSelectedVersionChanged(vm.SelectedVersion);
            vm.ConfirmVisible = confirm;
            if (vm._guideStatusKey is { } k) vm.GuideStatus = Loc.T(k);
            foreach (var p in new[] { nameof(InstalledBadge), nameof(DependencyNotices), nameof(DependencyFootnote), nameof(KeptNote) })
                vm.OnPropertyChanged(p);
            vm.NotifyExtrasChanged();
            vm.OnLanguageChanged();
        });
    }

    // Launcher i18n (spec § C): presentation in the active launcher language, per-field English fallback.
    private static string Lang => Loc.Service.ActiveLanguage;
    public string Name => Entry.DisplayName(Lang);
    public string Description => Entry.DisplayDescription(Lang);
    /// <summary>The guide shown: the active language's when published (<c>guideUrls</c>), else the English one.</summary>
    public string? GuideUrl => Entry.GuideUrlFor(Lang);
    /// <summary>The changelog cards: each version's changelog in the active language (section-level fallback).</summary>
    public IReadOnlyList<ChangelogVersionViewModel> ChangelogVersions =>
        Versions.Select(v => new ChangelogVersionViewModel(v.Version, v.Date, v.ChangelogFor(Lang))).ToList();

    private void OnLanguageChanged()
    {
        foreach (var p in new[] { nameof(Name), nameof(Description), nameof(Monogram), nameof(GuideUrl), nameof(ChangelogVersions), nameof(SelectedChangelog) })
            OnPropertyChanged(p);
        foreach (var m in Media) m.RefreshCaption();
        // The guide is per language: re-fetch when the page has already loaded one and the URL actually changed.
        if (_guideHttp is not null && GuideUrl != _loadedGuideUrl) _ = LoadGuideAsync(_guideHttp);
    }
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

    // List-card badge: the plugin's icon, else its first screenshot, else a monogram tile.
    [ObservableProperty] private Bitmap? _thumbnail;
    public bool ShowMonogram => Thumbnail is null;
    public string Monogram => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    partial void OnThumbnailChanged(Bitmap? value) => OnPropertyChanged(nameof(ShowMonogram));

    private bool _thumbnailRequested;

    public async Task LoadThumbnailAsync(HttpClient http)
    {
        if (_thumbnailRequested) return;
        _thumbnailRequested = true;
        var url = Entry.IconUrl
                  ?? Entry.Media?.FirstOrDefault(m =>
                      string.Equals(m?.Type, "image", StringComparison.OrdinalIgnoreCase)
                      && !string.IsNullOrWhiteSpace(m!.Url))?.Url;
        if (url is null) return;   // monogram tile stays
        try
        {
            var bytes = await http.GetByteArrayAsync(url);
            Thumbnail = await Task.Run(() =>
            {
                using var ms = new System.IO.MemoryStream(bytes);
                return Bitmap.DecodeToWidth(ms, 420);   // sharp enough for the 210px grid cover
            });
        }
        catch { /* badge falls back to the monogram */ }
    }
    [ObservableProperty] private string? _guideMarkdown;
    [ObservableProperty] private string _guideStatus = "";
    private string? _guideStatusKey;
    private void SetGuideStatus(string? key) { _guideStatusKey = key; GuideStatus = key is null ? "" : Loc.T(key); }
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
            for (var i = 0; i < media.Count; i++)
            {
                var m = media[i];
                var index = i;   // captions are translated BY INDEX into the manifest's media list
                if (!string.IsNullOrWhiteSpace(m?.Url))
                    Media.Add(new MediaItemViewModel(m!, http, openLightbox) { CaptionSource = () => Entry.MediaCaption(index, Lang) });
            }
            OnPropertyChanged(nameof(HasMedia));
            foreach (var tile in Media) _ = tile.LoadAsync();
        }
        _guideHttp = http;
        await LoadGuideAsync(http);
    }

    private HttpClient? _guideHttp;
    private string? _loadedGuideUrl;
    private int _guideGeneration;   // UI thread: only the newest guide request may apply its result

    /// <summary>Fetches <see cref="GuideUrl"/> (the active language's guide, else English). A translated guide that fails
    /// to download falls back to the English one. Only the newest request may apply its result.</summary>
    private async Task LoadGuideAsync(HttpClient http)
    {
        if (GuideUrl is not { } url) return;
        _loadedGuideUrl = url;
        var generation = ++_guideGeneration;
        SetGuideStatus("detail.guide.loading");
        try
        {
            string md;
            try { md = await http.GetStringAsync(url); }
            catch when (url != Entry.GuideUrl && Entry.GuideUrl is not null) { url = Entry.GuideUrl; md = await http.GetStringAsync(url); }
            if (generation != _guideGeneration) return;   // the language changed again while this was downloading
            GuideBaseUrl = url;
            GuideMarkdown = md;
            SetGuideStatus(null);
        }
        catch { if (generation == _guideGeneration) SetGuideStatus("detail.guide.unavailable"); }
    }

    /// <summary>The URL the shown guide was actually downloaded from — relative image paths resolve against it.</summary>
    [ObservableProperty] private string? _guideBaseUrl;

    // Selected version's changelog (may be null); the view guards visibility.
    public Changelog? SelectedChangelog => SelectedVersion?.ChangelogFor(Lang);

    // Canonical on-disk DLL filename for the selected version (for install/detect/remove).
    public string? CanonicalDll => SelectedVersion is { } v
        ? (v.Dll ?? System.IO.Path.GetFileName(new System.Uri(v.DllUrl).LocalPath))
        : null;

    public string InstalledBadge => InstalledVersion is { } iv ? Loc.TFormat("detail.installedV", iv) : Loc.T("detail.installed");

    // True when the newest registry version is strictly newer than what's installed.
    public bool HasUpdate => Installed && InstalledVersion is { } iv
        && Versions.Count > 0
        && VersionService.IsNewer(Versions[0].Version, iv);

    // A disabled plugin's row offers only Re-enable — Remove would be a silent no-op
    // (the on-disk copy lives under stellar/plugins-disabled/, not the scanned slot).
    public bool ShowRemove => Installed && !IsDisabled;

    partial void OnInstalledChanged(bool value)
    {
        OnPropertyChanged(nameof(InstalledBadge));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(ShowRemove));
    }
    partial void OnInstalledVersionChanged(string? value)
    {
        OnPropertyChanged(nameof(InstalledBadge));
        OnPropertyChanged(nameof(HasUpdate));
    }

    // Disabling parks the on-disk copy under plugins-disabled/ (version marker moves with it,
    // so InstalledVersion reads null) — force the install-state flags off so the row can't
    // offer Install/Reinstall/Update while disabled; only Re-enable should show.
    partial void OnIsDisabledChanged(bool value)
    {
        if (value)
        {
            IsPlainInstall = false;
            IsUpdate = false;
            IsReinstall = false;
        }
        OnPropertyChanged(nameof(ShowRemove));
    }

    partial void OnSelectedVersionChanged(PluginVersion? value)
    {
        ConfirmVisible = false;
        IsDowngrade = false;
        OnPropertyChanged(nameof(SelectedChangelog));

        if (value is null) { Compatible = false; CompatNote = ""; InstallLabel = Loc.T("common.install"); IsUpdate = false; IsReinstall = false; IsPlainInstall = false; return; }

        if (IsDisabled) { IsPlainInstall = false; IsUpdate = false; IsReinstall = false; CompatNote = ""; return; }

        if (_framework is null)
        {
            Compatible = false; CompatNote = Loc.T("detail.compat.noFramework"); InstallLabel = Loc.T("common.install"); IsUpdate = false; IsReinstall = false; IsPlainInstall = false;
            return;
        }

        Compatible = VersionService.IsModSystemCompatible(_framework, value.MinModSystemVersion, value.MaxModSystemVersion);
        if (!Compatible)
        {
            CompatNote = VersionService.IsNewer(value.MinModSystemVersion, _framework)
                ? Loc.TFormat("detail.compat.min", value.MinModSystemVersion)
                : Loc.TFormat("detail.compat.max", value.MaxModSystemVersion);
            InstallLabel = Loc.T("detail.incompatible"); IsUpdate = false; IsReinstall = false; IsPlainInstall = false;
            return;
        }

        CompatNote = "";
        if (!Installed)
            { InstallLabel = Loc.TFormat("ov.action.installV", value.Version); IsUpdate = false; IsReinstall = false; IsPlainInstall = true; }
        else if (InstalledVersion is null)
            { InstallLabel = Loc.TFormat("ov.action.reinstall", value.Version); IsUpdate = false; IsReinstall = true; IsPlainInstall = false; }   // present but unmanaged (no version marker) → adopt
        else if (VersionService.IsNewer(value.Version, InstalledVersion))
            { InstallLabel = Loc.TFormat("ov.action.updateTo", value.Version); IsUpdate = true; IsReinstall = false; IsPlainInstall = false; }
        else if (VersionService.IsNewer(InstalledVersion, value.Version))
            { InstallLabel = Loc.TFormat("ov.action.downgradeTo", value.Version); IsDowngrade = true; IsUpdate = false; IsReinstall = false; IsPlainInstall = false; }
        else
            { InstallLabel = Loc.TFormat("ov.action.reinstall", value.Version); IsUpdate = false; IsReinstall = true; IsPlainInstall = false; }
    }

    // Called by the parent after a successful install to refresh installed state + label.
    public void MarkInstalled(string version)
    {
        InstalledVersion = version;
        Installed = true;
        OnSelectedVersionChanged(SelectedVersion);   // recompute the label against the new installed version
        NotifyExtrasChanged();   // 2.1.2: ShowExtrasPill depends on Installed; ShownDependencies may have changed version
    }

    public void MarkRemoved()
    {
        InstalledVersion = null;
        Installed = false;
        OnSelectedVersionChanged(SelectedVersion);
        NotifyExtrasChanged();   // 2.1.2: no longer installed — the pill must disappear
    }

    // v3: when the click opens a step dialog, that dialog is the confirmation — no inline Confirm first.
    [RelayCommand]
    private Task RequestInstall()
    {
        if (!Compatible) return Task.CompletedTask;
        if (_parent.HasInstallStep(this)) return _parent.InstallAsync(this);
        ConfirmVisible = true;
        return Task.CompletedTask;
    }
    [RelayCommand] private void CancelInstall() => ConfirmVisible = false;
    [RelayCommand] private Task ConfirmInstall() { ConfirmVisible = false; return _parent.InstallAsync(this); }
    [RelayCommand] private Task Remove() => _parent.RemoveAsync(this);
    [RelayCommand] private Task ReEnable() => _parent.EnableAsync(this);
}

/// <summary>One changelog card on the plugin page: a version with its changelog already picked for the active language.</summary>
public sealed record ChangelogVersionViewModel(string Version, string? Date, Changelog? Changelog);

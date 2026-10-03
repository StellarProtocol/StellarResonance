using System;
using CommunityToolkit.Mvvm.ComponentModel;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;

namespace StellarLauncher.App.ViewModels;

/// <summary>One row of a plugin page's DEPENDENCIES section: what the manifest declares, where it goes, and
/// what the launcher's ledger/disk say about it. Only an optional dependency can be unticked.</summary>
public sealed partial class DependencyItemViewModel : ObservableObject
{
    private readonly Action<string, bool> _setUse;
    private bool _use;

    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private string _stateClass = "off";

    /// <param name="requiresLabel">Fix round M4: "with &lt;prerequisite names&gt;" for a dependency that has
    /// <c>requires</c>, shown as a chip; null otherwise.</param>
    public DependencyItemViewModel(PluginDependency d, DependencyStatus s, bool use, Action<string, bool> setUse,
        string? requiresLabel = null)
    {
        Dependency = d;
        _setUse = setUse;
        RequiresLabel = requiresLabel;
        _use = use || !d.Optional;   // a required dependency is always used
        ApplyStatus(s);
    }

    public PluginDependency Dependency { get; }
    public string Id => Dependency.Id;
    public string Name => Dependency.Name;
    public string Version => Dependency.Version;
    public string License => Dependency.License;
    public string? LicenseUrl => Dependency.LicenseUrl;
    public bool HasLicenseUrl => !string.IsNullOrWhiteSpace(LicenseUrl);
    public string? SourceUrl => Dependency.SourceUrl;
    public bool HasSourceUrl => !string.IsNullOrWhiteSpace(SourceUrl);
    public string? Notice => Dependency.Notice;
    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);
    public bool IsOptional => Dependency.Optional;
    public string? RequiresLabel { get; }
    public bool HasRequires => RequiresLabel is not null;

    /// <summary>Fix round M3: set while the client's game is running — no toggling then.</summary>
    [ObservableProperty] private bool _locked;
    partial void OnLockedChanged(bool value) => OnPropertyChanged(nameof(CanChange));
    /// <summary>What the checkbox's IsEnabled binds to: optional, and the game isn't running.</summary>
    public bool CanChange => IsOptional && !Locked;

    public string Where => string.Equals(Dependency.Target, "game", StringComparison.OrdinalIgnoreCase)
        ? (Dependency.ModdedOnly ? "game folder · Modded launches only" : "game folder")
        : "plugin folder";

    /// <summary>Ticked = install it. Toggling an optional dependency asks the host to record the choice;
    /// a required one — or any while <see cref="Locked"/> — ignores toggles (the checkbox is disabled then too).</summary>
    public bool Use
    {
        get => _use;
        set
        {
            if (!CanChange || value == _use) { OnPropertyChanged(); return; }   // re-sync a checkbox that tried to change
            SetProperty(ref _use, value);
            _setUse(Id, value);
        }
    }

    public bool IsOk => StateClass == "ok";
    public bool IsOff => StateClass == "off";
    public bool IsWarn => StateClass == "warn";
    public bool IsBad => StateClass == "bad";

    /// <summary>Refreshes the row in place (after a toggle or a background install) without re-raising
    /// the host callback — the row object, and the checkbox bound to it, stay the same.</summary>
    public void Update(DependencyStatus s, bool use)
    {
        SetProperty(ref _use, use || !IsOptional, nameof(Use));
        ApplyStatus(s);
    }

    private void ApplyStatus(DependencyStatus s)
    {
        StateText = s.State switch
        {
            DependencyState.Installed => "Installed",
            DependencyState.NotInstalled => "Not installed — installs at next Modded launch",
            // M-d: Detail is the prerequisite's NAME here (PluginItemViewModel resolves it from the id).
            DependencyState.Skipped when s.Reason == DependencyReason.WaitingForPrerequisite => $"Waiting for {s.Detail}",
            DependencyState.Skipped => "Skipped",
            DependencyState.Blocked when s.Reason == DependencyReason.OtherOwner => $"Blocked — another plugin's file is in the way: {s.Detail}",
            DependencyState.Blocked => $"Blocked — a file you installed is in the way: {s.Detail}",
            _ => $"Failed: {s.Detail}",
        };
        StateClass = s.State switch
        {
            DependencyState.Installed => "ok",
            DependencyState.NotInstalled or DependencyState.Skipped => "off",
            DependencyState.Blocked => "warn",
            _ => "bad",
        };
    }

    partial void OnStateClassChanged(string value)
    {
        OnPropertyChanged(nameof(IsOk)); OnPropertyChanged(nameof(IsOff));
        OnPropertyChanged(nameof(IsWarn)); OnPropertyChanged(nameof(IsBad));
    }
}

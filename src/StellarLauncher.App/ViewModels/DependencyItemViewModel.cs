using System;
using CommunityToolkit.Mvvm.ComponentModel;
using StellarLauncher.App.Localization;
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
        // The state pill is rendered from the last status: render it again in the new language.
        Loc.Subscribe(this, vm =>
        {
            if (vm._keptState is { } k) vm.ApplyKeptDiskState(k); else if (vm._status is { } st) vm.ApplyStatus(st);
            vm.OnPropertyChanged(nameof(Where));
        });
    }

    private DependencyStatus? _status;
    private KeptDependencyDiskState? _keptState;

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

    /// <summary>Final-review I-2: set while this row is shown from a KEPT ledger and the plugin itself isn't
    /// installed — there is nothing to use/skip right now (reinstalling the plugin adopts the ledger again,
    /// at which point the row is no longer kept).</summary>
    [ObservableProperty] private bool _keptLocked;
    partial void OnKeptLockedChanged(bool value) => OnPropertyChanged(nameof(CanChange));

    /// <summary>What the checkbox's IsEnabled binds to: optional, the game isn't running, and not a read-only kept row.</summary>
    public bool CanChange => IsOptional && !Locked && !KeptLocked;

    public string Where => string.Equals(Dependency.Target, "game", StringComparison.OrdinalIgnoreCase)
        ? (Dependency.ModdedOnly ? Loc.T("deps.where.gameModded") : Loc.T("deps.where.game"))
        : Loc.T("deps.where.plugin");

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
        _status = s; _keptState = null;
        StateText = s.State switch
        {
            DependencyState.Installed => Loc.T("deps.state.installed"),
            DependencyState.NotInstalled => Loc.T("deps.state.notInstalled"),
            // M-d: Detail is the prerequisite's NAME here (PluginItemViewModel resolves it from the id).
            DependencyState.Skipped when s.Reason == DependencyReason.WaitingForPrerequisite => Loc.TFormat("deps.state.waiting", s.Detail),
            DependencyState.Skipped => Loc.T("deps.state.skipped"),
            DependencyState.Blocked when s.Reason == DependencyReason.OtherOwner => Loc.TFormat("deps.state.blockedOther", s.Detail),
            DependencyState.Blocked => Loc.TFormat("deps.state.blockedUser", s.Detail),
            _ => Loc.TFormat("deps.state.failed", s.Detail),
        };
        StateClass = s.State switch
        {
            DependencyState.Installed => "ok",
            DependencyState.NotInstalled or DependencyState.Skipped => "off",
            DependencyState.Blocked => "warn",
            _ => "bad",
        };
    }

    /// <summary>Final-review I-2: overrides the generic "Installed" pill a kept row would otherwise get, with
    /// what's actually on disk right now.</summary>
    public void ApplyKeptDiskState(KeptDependencyDiskState state)
    {
        _keptState = state;
        StateText = state switch
        {
            KeptDependencyDiskState.Kept => Loc.T("deps.state.kept"),
            KeptDependencyDiskState.Parked => Loc.T("deps.state.keptParked"),
            _ => Loc.T("deps.state.missing"),
        };
        StateClass = state switch
        {
            KeptDependencyDiskState.Kept => "ok",
            // R-5: moved aside is the NORMAL state after a Vanilla launch, not a problem — the neutral "off"
            // outline, not the amber warning used for an actual Blocked outcome elsewhere on this page.
            KeptDependencyDiskState.Parked => "off",
            _ => "bad",
        };
    }

    partial void OnStateClassChanged(string value)
    {
        OnPropertyChanged(nameof(IsOk)); OnPropertyChanged(nameof(IsOff));
        OnPropertyChanged(nameof(IsWarn)); OnPropertyChanged(nameof(IsBad));
    }
}

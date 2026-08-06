using CommunityToolkit.Mvvm.ComponentModel;
using StellarLauncher.Core.Model;

namespace StellarLauncher.App.ViewModels;

// One plugin row in the review. Carries the registry Entry + resolved target PluginVersion so the
// review VM can install it. IsSelected = update it; IsDisableChosen = park it for launch.
public partial class PluginPlanRowViewModel : ObservableObject
{
    public PluginPlanItem Item { get; }
    public PluginEntry Entry { get; }
    public PluginVersion? Target { get; }   // the version to install when selected (null for unfixable)

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isDisableChosen;
    [ObservableProperty] private double _percent;
    [ObservableProperty] private string _statusText = "";

    public PluginPlanRowViewModel(PluginPlanItem item, PluginEntry entry, PluginVersion? target, bool preselect)
    {
        Item = item; Entry = entry; Target = target;
        _isSelected = preselect && item.NeedsUpdate;
    }

    public string Name => Item.Name;
    public string VersionLabel => Item.TargetVersion is { } t
        ? $"v{Item.InstalledVersion ?? "?"} → v{t}"
        : $"v{Item.InstalledVersion ?? "?"}";
    public PluginPlanStatus Status => Item.Status;
    public bool IsUnfixable => Item.Status == PluginPlanStatus.UnfixableIncompatible;
    public bool CanUpdate => Item.NeedsUpdate && Target is not null;

    // A row is "resolved" (won't block launch) when it doesn't block, or it's being updated, or disabled.
    public bool IsResolved => !Item.Blocks || (CanUpdate && IsSelected) || IsDisableChosen;

    partial void OnIsSelectedChanged(bool value) => ResolvedChanged?.Invoke();
    partial void OnIsDisableChosenChanged(bool value) => ResolvedChanged?.Invoke();
    public System.Action? ResolvedChanged;
}

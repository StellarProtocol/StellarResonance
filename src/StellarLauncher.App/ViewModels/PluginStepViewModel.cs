using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.App.Localization;
using StellarLauncher.App.Services;

namespace StellarLauncher.App.ViewModels;

public enum PluginStepKind { Install, Reinstall, Remove }

/// <summary>One install-step checkbox row (V1): ticked by default — unless review fix round 2 (b)'s
/// <see cref="InstallStepOption.InitiallyUnticked"/> says the player already opted out of it.</summary>
public sealed partial class InstallOptionViewModel : ObservableObject
{
    public InstallOptionViewModel(InstallStepOption option, string pluginName)
    {
        Option = option;
        NoticeText = string.IsNullOrWhiteSpace(option.Notice) ? null : Loc.TFormat("deps.notice", pluginName, option.Notice.Trim());
        Use = !option.InitiallyUnticked;
    }

    public InstallStepOption Option { get; }
    public string Title => Option.Title;
    public string Detail => Option.Detail;
    public string? NoticeText { get; }
    public bool HasNotice => NoticeText is not null;
    [ObservableProperty] private bool _use = true;
}

/// <summary>v3 (spec § 12, mockup 2026-10-04 v3): the state and wording of one plugin step dialog. Completion is true for
/// the OK button, false for Cancel or a window-manager close (<see cref="CancelIfUnfinished"/>) — whichever comes first.</summary>
public sealed partial class PluginStepViewModel : ObservableObject
{
    private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private PluginStepViewModel(PluginStepKind kind, string title, string body, string okLabel)
    {
        Kind = kind; Title = title; Body = body; OkLabel = okLabel;
    }

    public PluginStepKind Kind { get; }
    public bool IsInstall => Kind == PluginStepKind.Install;
    public bool IsReinstall => Kind == PluginStepKind.Reinstall;
    public bool IsRemove => Kind == PluginStepKind.Remove;
    public string Title { get; }
    public string Body { get; }
    public string OkLabel { get; }

    // V1
    public ObservableCollection<InstallOptionViewModel> Options { get; } = new();

    // V2 — unticked by default
    public string ReinstallDetail { get; private init; } = "";
    [ObservableProperty] private bool _alsoReinstallDependencies;

    // V3 — "remove the plugin and its dependencies" by default
    public string RemoveAllTitle { get; private init; } = "";
    public string RemoveAllDetail { get; private init; } = "";
    public string RemoveOnlyTitle { get; private init; } = "";
    public string RemoveOnlyDetail { get; private init; } = "";
    [ObservableProperty] private bool _removeDependencies = true;
    /// <summary>The second radio button: the inverse of <see cref="RemoveDependencies"/>.</summary>
    public bool KeepDependencies { get => !RemoveDependencies; set => RemoveDependencies = !value; }
    partial void OnRemoveDependenciesChanged(bool value) => OnPropertyChanged(nameof(KeepDependencies));

    public Task<bool> Completion => _done.Task;
    public event Action? RequestClose;

    [RelayCommand] private void Ok() { _done.TrySetResult(true); RequestClose?.Invoke(); }
    [RelayCommand] private void Cancel() { _done.TrySetResult(false); RequestClose?.Invoke(); }
    /// <summary>Window-manager close (title-bar X / Alt+F4) bypasses the commands; a no-op once answered.</summary>
    public void CancelIfUnfinished() => _done.TrySetResult(false);

    public InstallStepResult InstallResult() =>
        new(Options.Where(o => !o.Use).Select(o => o.Option.DependencyId).ToHashSet(StringComparer.Ordinal));
    public RemoveChoice RemoveResult() => RemoveDependencies ? RemoveChoice.PluginAndDependencies : RemoveChoice.PluginOnly;

    public static PluginStepViewModel ForInstall(InstallStep s)
    {
        var vm = new PluginStepViewModel(PluginStepKind.Install, Loc.TFormat("step.install.title", s.PluginName, s.Version),
            Loc.TFormat("step.install.body", s.PluginName), Loc.T("common.install"));
        foreach (var o in s.Options) vm.Options.Add(new InstallOptionViewModel(o, s.PluginName));
        return vm;
    }

    public static PluginStepViewModel ForReinstall(ReinstallStep s) =>
        new(PluginStepKind.Reinstall, Loc.TFormat("step.reinstall.title", s.PluginName, s.Version),
            Loc.TFormat("step.reinstall.body", s.PluginName), Loc.T("step.reinstall.ok"))
        {
            ReinstallDetail = Loc.TFormat("step.reinstall.detail", PluginStepText.JoinNames(s.DependencyNames)),
        };

    public static PluginStepViewModel ForRemove(RemoveStep s)
    {
        var names = PluginStepText.JoinNames(s.DependencyNames);
        var aside = s.AnyModdedOnly ? Loc.T("step.remove.aside") : "";
        return new(PluginStepKind.Remove, Loc.TFormat("step.remove.title", s.PluginName), Loc.T("step.remove.body"), Loc.T("common.remove"))
        {
            RemoveAllTitle = Loc.TFormat("step.remove.allTitle", s.PluginName),
            RemoveAllDetail = Loc.TFormat("step.remove.allDetail", names),
            RemoveOnlyTitle = Loc.TFormat("step.remove.onlyTitle", s.PluginName),
            RemoveOnlyDetail = Loc.TFormat(s.Plural ? "step.remove.onlyDetail.other" : "step.remove.onlyDetail.one", PluginStepText.Sentence(names), aside),
        };
    }
}

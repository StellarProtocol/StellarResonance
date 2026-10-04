using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels;

namespace StellarLauncher.App.Views;

/// <summary>v3 (spec § 12): the install / reinstall / remove step, modal on the main window. Like
/// <see cref="ConfirmDialog"/>, the instance handed to the composition root is a factory — each question opens a fresh
/// window. Never blocks: the caller awaits the view-model's completion (a UI-thread wait would deadlock).
/// Review fix round 1 (a): UI-thread only — every <c>Ask*Async</c> call must reach <see cref="ShowAsync"/> without a
/// prior <c>ConfigureAwait(false)</c> (every caller in <see cref="PluginInstallFlow"/> plainly awaits it, so this holds
/// today); <see cref="ShowAsync"/> asserts it with <see cref="Dispatcher"/>.<see cref="Dispatcher.UIThread"/>'s
/// <c>VerifyAccess()</c> rather than relying on that convention alone.</summary>
public partial class PluginStepDialog : Window, IPluginSteps
{
    private readonly Func<Window?> _owner;
    public PluginStepDialog() : this(() => null) { }
    public PluginStepDialog(Func<Window?> owner) { _owner = owner; AvaloniaXamlLoader.Load(this); }

    public async Task<InstallStepResult?> AskInstallAsync(InstallStep step)
    {
        var vm = PluginStepViewModel.ForInstall(step);
        return await ShowAsync(vm) ? vm.InstallResult() : null;
    }

    public async Task<bool?> AskReinstallAsync(ReinstallStep step)
    {
        var vm = PluginStepViewModel.ForReinstall(step);
        return await ShowAsync(vm) ? vm.AlsoReinstallDependencies : (bool?)null;
    }

    public async Task<RemoveChoice?> AskRemoveAsync(RemoveStep step)
    {
        var vm = PluginStepViewModel.ForRemove(step);
        return await ShowAsync(vm) ? vm.RemoveResult() : (RemoveChoice?)null;
    }

    /// <summary>No owner window yet = treated as Cancel (nothing changes). UI-thread only — see the class doc.</summary>
    private async Task<bool> ShowAsync(PluginStepViewModel vm)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_owner() is not { } owner) return false;
        var dlg = new PluginStepDialog(_owner) { DataContext = vm };
        vm.RequestClose += () => dlg.Close();
        dlg.Closed += (_, _) => vm.CancelIfUnfinished();
        var shown = dlg.ShowDialog(owner);
        var ok = await vm.Completion;
        await shown;
        return ok;
    }
}

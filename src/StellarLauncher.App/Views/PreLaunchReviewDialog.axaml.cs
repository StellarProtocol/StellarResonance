using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StellarLauncher.App.ViewModels;

namespace StellarLauncher.App.Views;

public partial class PreLaunchReviewDialog : Window
{
    public PreLaunchReviewDialog() => AvaloniaXamlLoader.Load(this);

    public static async Task<PreLaunchResult> ShowFor(PreLaunchReviewViewModel vm, Window owner)
    {
        var dlg = new PreLaunchReviewDialog { DataContext = vm };
        vm.RequestClose += () => dlg.Close();
        // Window-manager close (title-bar X / Alt+F4) bypasses every RelayCommand, so without this
        // Completion never resolves and the caller hangs forever. TrySetResult inside is a no-op if
        // Cancel/Launch/UpdateAllAndLaunch already completed it.
        dlg.Closed += (_, _) => vm.CancelIfUnfinished();
        // Auto-update ON: begin updating + launching the moment the dialog is shown, without a click
        // (owner Image #17). No-op when a decision is required (an unfixable plugin to disable) or when
        // auto-update is off — those keep the buttons.
        dlg.Opened += async (_, _) => await vm.StartIfAutoAsync();
        var shown = dlg.ShowDialog(owner);          // modal; closes on RequestClose
        var result = await vm.Completion;           // set by the VM before it raises RequestClose
        await shown;
        return result;
    }
}

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
        var shown = dlg.ShowDialog(owner);          // modal; closes on RequestClose
        var result = await vm.Completion;           // set by the VM before it raises RequestClose
        await shown;
        return result;
    }
}

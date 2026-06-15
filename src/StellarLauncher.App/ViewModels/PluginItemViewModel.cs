using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarLauncher.Core.Model;

namespace StellarLauncher.App.ViewModels;

public partial class PluginItemViewModel : ObservableObject
{
    private readonly PluginsViewModel _parent;
    public PluginEntry Entry { get; }

    [ObservableProperty] private bool _installed;
    [ObservableProperty] private string _action = "";

    public PluginItemViewModel(PluginEntry entry, bool installed, PluginsViewModel parent)
    {
        Entry = entry; _installed = installed; _parent = parent;
    }

    public string Name => Entry.Name;
    public string Description => Entry.Description;
    public string Version => Entry.Version;
    public string Author => Entry.Author ?? "";

    [RelayCommand] private Task Install() => _parent.InstallAsync(this);
    [RelayCommand] private Task Remove()  => _parent.RemoveAsync(this);
}

using System.Threading.Tasks;

namespace StellarLauncher.App.ViewModels;

/// <summary>What a plugin row asks its host to do (install the selected version, remove, re-enable).</summary>
public interface IPluginActions
{
    Task InstallAsync(PluginItemViewModel item);
    Task RemoveAsync(PluginItemViewModel item);
    Task EnableAsync(PluginItemViewModel item);
}

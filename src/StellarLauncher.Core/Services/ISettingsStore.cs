// src/StellarLauncher.Core/Services/ISettingsStore.cs
namespace StellarLauncher.Core.Services;

public interface ISettingsStore
{
    LauncherSettings Load();
    void Save(LauncherSettings settings);
}

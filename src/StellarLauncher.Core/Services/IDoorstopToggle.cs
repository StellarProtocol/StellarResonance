// src/StellarLauncher.Core/Services/IDoorstopToggle.cs
namespace StellarLauncher.Core.Services;

public interface IDoorstopToggle
{
    bool IsEnabled(string doorstopConfigPath);
    void SetEnabled(string doorstopConfigPath, bool enabled);
}

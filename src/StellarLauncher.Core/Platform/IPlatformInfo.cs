// src/StellarLauncher.Core/Platform/IPlatformInfo.cs
namespace StellarLauncher.Core.Platform;

public interface IPlatformInfo
{
    bool IsWindows { get; }
    string AppDataDir { get; } // per-user config dir for launcher settings
}

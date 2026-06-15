// src/StellarLauncher.Core/Platform/PlatformInfo.cs
using System;
using System.Runtime.InteropServices;

namespace StellarLauncher.Core.Platform;

public sealed class PlatformInfo : IPlatformInfo
{
    public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public string AppDataDir =>
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
}

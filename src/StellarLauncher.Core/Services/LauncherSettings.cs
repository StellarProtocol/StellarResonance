// src/StellarLauncher.Core/Services/LauncherSettings.cs
using System.Collections.Generic;

namespace StellarLauncher.Core.Services;

public sealed class LauncherSettings
{
    public string? GameMiniDir { get; set; }
    public string? Runner { get; set; }      // Linux only
    public string? WinePrefix { get; set; }  // Linux only
    public bool Modded { get; set; } = true;
    public List<string> ExtraPluginRepos { get; set; } = new();
    public string Channel { get; set; } = "stable";   // "stable" | "testing"
}

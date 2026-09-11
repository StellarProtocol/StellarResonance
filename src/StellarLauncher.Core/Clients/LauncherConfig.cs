using System;
using System.Collections.Generic;
using System.Linq;

namespace StellarLauncher.Core.Clients;

/// <summary>Launcher-wide preferences — nothing here is about a specific install.</summary>
public sealed class LauncherOptions
{
    public string Channel { get; set; } = "stable";           // the LAUNCHER's own update channel
    public List<string> PluginSources { get; set; } = new();   // extra registry URLs, all clients
    public bool KeepOpen { get; set; } = true;
    public string StartOn { get; set; } = "dashboard";        // "dashboard" | "lastClient"
    public bool ShowMatrix { get; set; } = true;
    public string? LastSelectedClientId { get; set; }
    public List<string> DismissedDetections { get; set; } = new();
}

/// <summary>settings.json v2 root.</summary>
public sealed class LauncherConfig
{
    public int Version { get; set; } = 2;
    public LauncherOptions Launcher { get; set; } = new();
    public List<ClientProfile> Clients { get; set; } = new();

    public ClientProfile? FindById(string id) => Clients.FirstOrDefault(c => c.Id == id);

    /// <summary>True when another client already uses <paramref name="name"/> (case-insensitive).</summary>
    public bool NameTaken(string name, string? exceptId = null) =>
        Clients.Any(c => c.Id != exceptId && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}

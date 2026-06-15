using System.Collections.Generic;

namespace StellarLauncher.Core.Services;

public interface IGameDetector
{
    /// <summary>Probe known install locations and return every game_mini directory found.</summary>
    IReadOnlyList<string> Detect();

    /// <summary>First existing Wine/Proton runner binary from known install spots, or null.</summary>
    string? DetectRunner();

    /// <summary>First existing umu-run launcher binary (preferred for Proton), or null.</summary>
    string? DetectUmu();
}

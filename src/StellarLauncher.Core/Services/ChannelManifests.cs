using System;

namespace StellarLauncher.Core.Services;

/// <summary>Maps an update channel to the MinIO manifest URLs the launcher fetches.</summary>
public static class ChannelManifests
{
    private const string Base = "https://minio.revette.io/stellar";

    public static bool IsTesting(string? channel)
        => string.Equals(channel, "testing", StringComparison.OrdinalIgnoreCase);

    public static Uri FrameworkVersion(string? channel)
        => new($"{Base}/{(IsTesting(channel) ? "version-testing.json" : "version.json")}");

    public static Uri LauncherManifest(string? channel)
        => new($"{Base}/{(IsTesting(channel) ? "launcher-testing.json" : "launcher.json")}");
}

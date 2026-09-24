using System;

namespace StellarLauncher.Core.Services;

/// <summary>Maps an update channel to the MinIO manifest URLs the launcher fetches.</summary>
public static class ChannelManifests
{
    // The CDN the launcher reads manifests from. Overridable via STELLAR_CDN_BASE (point it at a local or
    // staging server to exercise the update flow — e.g. verify the launcher picks up a version published
    // while it is already running); unset → production. A trailing slash is tolerated.
    internal static string Base =>
        Environment.GetEnvironmentVariable("STELLAR_CDN_BASE") is { Length: > 0 } b
            ? b.TrimEnd('/')
            : "https://cdn.revette.io";

    public static bool IsTesting(string? channel)
        => string.Equals(channel, "testing", StringComparison.OrdinalIgnoreCase);

    public static Uri FrameworkVersion(string? channel)
        => new($"{Base}/{(IsTesting(channel) ? "version-testing.json" : "version.json")}");

    public static Uri LauncherManifest(string? channel)
        => new($"{Base}/{(IsTesting(channel) ? "launcher-testing.json" : "launcher.json")}");

    /// <summary>Curated plugin registry for the channel. testing = a superset (stable + testing-only plugins).</summary>
    public static Uri PluginRegistry(string? channel)
        => new($"{Base}/{(IsTesting(channel) ? "plugins-testing.json" : "plugins.json")}");
}

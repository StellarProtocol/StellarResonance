// src/StellarLauncher.Core/Services/VersionService.cs
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Services;

public sealed class VersionService : IVersionService
{
    private readonly HttpClient _http;
    public VersionService(HttpClient http) => _http = http;

    public async Task<FrameworkManifest> FetchAsync(Uri manifestUrl, CancellationToken ct = default)
    {
        var manifest = await _http.GetFromJsonAsync<FrameworkManifest>(
            manifestUrl, FrameworkManifest.JsonOptions, ct);
        if (manifest?.Versions is null || manifest.Versions.Count == 0)
            throw new InvalidOperationException("empty version manifest");
        return manifest;
    }

    /// <summary>True when <paramref name="remote"/> is a strictly higher semver than <paramref name="installed"/>.</summary>
    public static bool IsNewer(string remote, string installed)
        => Parse(remote) > Parse(installed);

    /// <summary>True when the launcher meets the framework's minimum.</summary>
    public static bool LauncherSupported(string minLauncher, string launcherVersion)
        => Parse(launcherVersion) >= Parse(minLauncher);

    /// <summary>
    /// True when a plugin build that requires framework in [min, max] can run on the installed framework.
    /// <paramref name="maxModSystem"/> null means no upper bound. See docs/manifest-standard.md.
    /// </summary>
    public static bool IsModSystemCompatible(string installedFramework, string minModSystem, string? maxModSystem)
    {
        var f = Parse(installedFramework);
        if (f < Parse(minModSystem)) return false;
        if (!string.IsNullOrWhiteSpace(maxModSystem) && f > Parse(maxModSystem)) return false;
        return true;
    }

    private static Version Parse(string v) => Version.Parse(v.TrimStart('v', 'V'));
}

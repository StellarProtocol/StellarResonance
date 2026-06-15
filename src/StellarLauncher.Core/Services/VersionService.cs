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

    public async Task<VersionManifest> FetchAsync(Uri manifestUrl, CancellationToken ct = default)
    {
        var manifest = await _http.GetFromJsonAsync<VersionManifest>(
            manifestUrl, VersionManifest.JsonOptions, ct);
        return manifest ?? throw new InvalidOperationException("empty version manifest");
    }

    /// <summary>True when <paramref name="remote"/> is a strictly higher semver than <paramref name="installed"/>.</summary>
    public static bool IsNewer(string remote, string installed)
        => Parse(remote) > Parse(installed);

    /// <summary>True when the launcher meets the framework's minimum.</summary>
    public static bool LauncherSupported(string minLauncher, string launcherVersion)
        => Parse(launcherVersion) >= Parse(minLauncher);

    private static Version Parse(string v) => Version.Parse(v.TrimStart('v'));
}

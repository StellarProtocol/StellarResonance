using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.Services;

/// <summary>Framework version manifest per channel, cached for a refresh cycle; null when offline.</summary>
public sealed class FrameworkManifests
{
    private readonly IVersionService _versions;
    private readonly Dictionary<string, FrameworkManifest?> _cache = new();

    public FrameworkManifests(IVersionService versions) => _versions = versions;

    public async Task<FrameworkManifest?> LatestForAsync(string channel, CancellationToken ct)
    {
        var key = ChannelManifests.IsTesting(channel) ? "testing" : "stable";
        if (_cache.TryGetValue(key, out var m)) return m;
        try { m = await _versions.FetchAsync(ChannelManifests.FrameworkVersion(key), ct); }
        catch (Exception) { m = null; }
        _cache[key] = m;
        return m;
    }

    public void Invalidate() => _cache.Clear();
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.Services;

/// <summary>
/// Framework version manifest per channel, cached with a short TTL so a long-lived launcher session picks up
/// a newly released version without a restart. Rapid navigation (tab/client switches) inside the TTL reuses
/// the cached fetch; after the TTL the next read re-fetches. <see cref="Invalidate"/> forces an immediate
/// re-fetch (the Dashboard's pull-to-refresh). Null when offline.
/// </summary>
public sealed class FrameworkManifests
{
    /// <summary>How long a fetched manifest is served before the next read re-fetches.</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);

    private readonly IVersionService _versions;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _ttl;
    private readonly Dictionary<string, (FrameworkManifest? Manifest, DateTimeOffset FetchedAt)> _cache = new();

    public FrameworkManifests(IVersionService versions, Func<DateTimeOffset>? now = null, TimeSpan? ttl = null)
    {
        _versions = versions;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _ttl = ttl ?? DefaultTtl;
    }

    public async Task<FrameworkManifest?> LatestForAsync(string channel, CancellationToken ct)
    {
        var key = ChannelManifests.IsTesting(channel) ? "testing" : "stable";
        if (_cache.TryGetValue(key, out var e) && _now() - e.FetchedAt < _ttl) return e.Manifest;
        FrameworkManifest? m;
        try { m = await _versions.FetchAsync(ChannelManifests.FrameworkVersion(key), ct); }
        catch (Exception) { m = null; }
        _cache[key] = (m, _now());
        return m;
    }

    public void Invalidate() => _cache.Clear();
}

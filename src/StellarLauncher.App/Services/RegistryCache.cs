using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.Services;

/// <summary>Plugin registry per channel: stable, or testing layered over stable, plus the launcher-wide
/// extra sources. Cached with a short TTL so a long-lived session picks up newly published plugin versions
/// without a restart; rapid navigation inside the TTL reuses the fetch. <see cref="Invalidate"/> forces an
/// immediate re-fetch (the Dashboard's pull-to-refresh).</summary>
public sealed class RegistryCache
{
    /// <summary>How long a fetched registry is served before the next read re-fetches.</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);

    private readonly IPluginRegistryService _registry;
    private readonly Func<LauncherConfig> _config;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _ttl;
    private readonly Dictionary<string, (IReadOnlyList<PluginEntry> Entries, DateTimeOffset FetchedAt)> _byChannel = new();

    public RegistryCache(IPluginRegistryService registry, Func<LauncherConfig> config,
        Func<DateTimeOffset>? now = null, TimeSpan? ttl = null)
    {
        _registry = registry; _config = config;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _ttl = ttl ?? DefaultTtl;
    }

    public async Task<IReadOnlyList<PluginEntry>> ForChannelAsync(string channel, CancellationToken ct)
    {
        var key = ChannelManifests.IsTesting(channel) ? "testing" : "stable";
        if (_byChannel.TryGetValue(key, out var e) && _now() - e.FetchedAt < _ttl) return e.Entries;
        var entries = await _registry.FetchAllAsync(UrlsFor(key, _config().Launcher.PluginSources), ct);
        _byChannel[key] = (entries, _now());
        return entries;
    }

    public void Invalidate() => _byChannel.Clear();

    public static IReadOnlyList<Uri> UrlsFor(string channel, IEnumerable<string> extraSources)
    {
        var urls = new List<Uri> { ChannelManifests.PluginRegistry(null) };
        if (ChannelManifests.IsTesting(channel)) urls.Add(ChannelManifests.PluginRegistry("testing"));
        foreach (var s in extraSources)
            if (Uri.TryCreate(s, UriKind.Absolute, out var u)) urls.Add(u);
        return urls;
    }
}

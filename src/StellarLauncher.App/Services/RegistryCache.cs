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
/// extra sources. Fetched once per refresh cycle; <see cref="Invalidate"/> on Reload.</summary>
public sealed class RegistryCache
{
    private readonly IPluginRegistryService _registry;
    private readonly Func<LauncherConfig> _config;
    private readonly Dictionary<string, IReadOnlyList<PluginEntry>> _byChannel = new();

    public RegistryCache(IPluginRegistryService registry, Func<LauncherConfig> config)
    {
        _registry = registry; _config = config;
    }

    public async Task<IReadOnlyList<PluginEntry>> ForChannelAsync(string channel, CancellationToken ct)
    {
        var key = ChannelManifests.IsTesting(channel) ? "testing" : "stable";
        if (_byChannel.TryGetValue(key, out var cached)) return cached;
        var entries = await _registry.FetchAllAsync(UrlsFor(key, _config().Launcher.PluginSources), ct);
        _byChannel[key] = entries;
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

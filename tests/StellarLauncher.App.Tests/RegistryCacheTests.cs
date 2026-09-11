using StellarLauncher.App.Services;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;
using Xunit;

public class RegistryCacheTests
{
    private sealed class Fake : IPluginRegistryService
    {
        public readonly List<IReadOnlyList<Uri>> Calls = new();
        public Task<IReadOnlyList<PluginEntry>> FetchAllAsync(IEnumerable<Uri> urls, CancellationToken ct = default)
        { Calls.Add(urls.ToList()); return Task.FromResult<IReadOnlyList<PluginEntry>>(Array.Empty<PluginEntry>()); }
    }

    [Fact]
    public void UrlsFor_stable_is_stable_only_plus_sources_and_testing_layers_over_stable()
    {
        Assert.Equal(new[] { ChannelManifests.PluginRegistry(null), new Uri("https://x/m.json") },
            RegistryCache.UrlsFor("stable", new[] { "https://x/m.json", "not a url" }));
        Assert.Equal(new[] { ChannelManifests.PluginRegistry(null), ChannelManifests.PluginRegistry("testing") },
            RegistryCache.UrlsFor("testing", Array.Empty<string>()));
    }

    [Fact]
    public async Task Caches_per_channel_until_invalidated()
    {
        var fake = new Fake();
        var cache = new RegistryCache(fake, () => new LauncherConfig());
        await cache.ForChannelAsync("stable", CancellationToken.None);
        await cache.ForChannelAsync("stable", CancellationToken.None);
        await cache.ForChannelAsync("testing", CancellationToken.None);
        Assert.Equal(2, fake.Calls.Count);
        cache.Invalidate();
        await cache.ForChannelAsync("stable", CancellationToken.None);
        Assert.Equal(3, fake.Calls.Count);
    }
}

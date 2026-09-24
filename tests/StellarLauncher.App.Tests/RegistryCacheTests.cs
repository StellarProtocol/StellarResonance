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

    [Fact]
    public async Task Refetches_after_the_ttl_elapses()
    {
        var fake = new Fake();
        var now = new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
        var cache = new RegistryCache(fake, () => new LauncherConfig(), () => now, TimeSpan.FromSeconds(60));

        await cache.ForChannelAsync("stable", CancellationToken.None);   // fetch #1
        now = now.AddSeconds(30);
        await cache.ForChannelAsync("stable", CancellationToken.None);   // within TTL → cached
        Assert.Single(fake.Calls);

        now = now.AddSeconds(31);                                        // now 61s since fetch → expired
        await cache.ForChannelAsync("stable", CancellationToken.None);   // fetch #2
        Assert.Equal(2, fake.Calls.Count);
    }
}

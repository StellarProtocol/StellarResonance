using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.App.Services;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;
using Xunit;

public class FrameworkManifestsTests
{
    private sealed class Fake : IVersionService
    {
        public int Calls;
        public string Latest = "2.8.3";
        public Task<FrameworkManifest> FetchAsync(Uri manifestUrl, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new FrameworkManifest(Latest, null, Array.Empty<VersionManifest>()));
        }
    }

    [Fact]
    public async Task Serves_cache_within_ttl_then_refetches_the_new_version()
    {
        var fake = new Fake();
        var now = new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
        var manifests = new FrameworkManifests(fake, () => now, TimeSpan.FromSeconds(60));

        Assert.Equal("2.8.3", (await manifests.LatestForAsync("stable", CancellationToken.None))?.Latest);
        Assert.Equal(1, fake.Calls);

        // A new framework is released while the launcher stays open.
        fake.Latest = "2.9.0";
        now = now.AddSeconds(30);
        Assert.Equal("2.8.3", (await manifests.LatestForAsync("stable", CancellationToken.None))?.Latest); // cached
        Assert.Equal(1, fake.Calls);

        now = now.AddSeconds(31);   // 61s since fetch → TTL expired
        Assert.Equal("2.9.0", (await manifests.LatestForAsync("stable", CancellationToken.None))?.Latest); // re-fetched
        Assert.Equal(2, fake.Calls);
    }

    [Fact]
    public async Task Invalidate_forces_an_immediate_refetch()
    {
        var fake = new Fake();
        var now = new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
        var manifests = new FrameworkManifests(fake, () => now, TimeSpan.FromSeconds(60));

        await manifests.LatestForAsync("stable", CancellationToken.None);
        Assert.Equal(1, fake.Calls);

        fake.Latest = "2.9.0";
        manifests.Invalidate();
        Assert.Equal("2.9.0", (await manifests.LatestForAsync("stable", CancellationToken.None))?.Latest);
        Assert.Equal(2, fake.Calls);
    }

    [Fact]
    public async Task Offline_fetch_is_null_and_not_cached_as_success()
    {
        var throwing = new Throwing();
        var now = new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
        var manifests = new FrameworkManifests(throwing, () => now, TimeSpan.FromSeconds(60));

        Assert.Null(await manifests.LatestForAsync("stable", CancellationToken.None));
        // A failed fetch is cached for the TTL too (as null) — it must not throw or loop.
        Assert.Null(await manifests.LatestForAsync("stable", CancellationToken.None));
    }

    private sealed class Throwing : IVersionService
    {
        public Task<FrameworkManifest> FetchAsync(Uri manifestUrl, CancellationToken ct = default)
            => throw new InvalidOperationException("offline");
    }
}

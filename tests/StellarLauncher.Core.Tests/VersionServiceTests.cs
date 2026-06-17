using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Services;
using Xunit;

public class VersionServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        public StubHandler(string body) => _body = body;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
               { Content = new StringContent(_body) });
    }

    [Fact]
    public async Task Fetches_and_parses_version_history()
    {
        var json = """
        {"latest":"1.2.0","channel":"stable","versions":[
          {"version":"1.2.0","date":"2026-06-08","bundleUrl":"u2","sha256":"s2",
           "minLauncherVersion":"1.0.0",
           "changelog":{"added":["a"],"changed":[],"fixed":[],"removed":[]}},
          {"version":"1.1.0","date":"2026-06-01","bundleUrl":"u1","sha256":"s1",
           "minLauncherVersion":"1.0.0",
           "changelog":{"added":[],"changed":[],"fixed":["f"],"removed":[]}}
        ]}
        """;
        var svc = new VersionService(new HttpClient(new StubHandler(json)));
        var m = await svc.FetchAsync(new Uri("https://x/version.json"));
        Assert.Equal("1.2.0", m.Latest);
        Assert.Equal(2, m.Versions.Count);
        Assert.Equal("1.2.0", m.Versions[0].Version);
        Assert.Equal("u1", m.Versions[1].BundleUrl);
    }

    [Theory]
    [InlineData("1.4.0", "1.4.0", null, true)]   // exactly at min, no max
    [InlineData("1.5.0", "1.4.0", null, true)]   // above min, no max
    [InlineData("1.3.0", "1.4.0", null, false)]  // below min -> needs newer framework
    [InlineData("1.4.0", "1.2.0", "1.4.0", true)]  // within [min,max]
    [InlineData("1.5.0", "1.2.0", "1.4.0", false)] // above max -> framework too new for this plugin build
    public void IsModSystemCompatible(string framework, string min, string? max, bool expected)
        => Assert.Equal(expected, VersionService.IsModSystemCompatible(framework, min, max));

    [Theory]
    [InlineData("1.1.0", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("0.9.0", "1.0.0", false)]
    public void IsNewer_compares_semver(string remote, string installed, bool expected)
        => Assert.Equal(expected, VersionService.IsNewer(remote, installed));

    [Theory]
    [InlineData("1.0.0", "1.0.0", true)]
    [InlineData("1.1.0", "1.0.0", false)] // launcher older than required
    public void LauncherSupported(string minLauncher, string launcher, bool expected)
        => Assert.Equal(expected, VersionService.LauncherSupported(minLauncher, launcher));

    [Theory]
    [InlineData("1.2.3", "1.2.3-rc.1", false)]   // pre-release build of the SAME version is NOT newer
    [InlineData("1.2.4", "1.2.3-dev", true)]     // pre-release suffix doesn't break the comparison
    [InlineData("v1.2.0+abc123", "1.1.0", true)] // leading "v" + "+build" metadata tolerated
    public void IsNewer_tolerates_prerelease_and_build_metadata(string remote, string installed, bool expected)
        => Assert.Equal(expected, VersionService.IsNewer(remote, installed));

    [Theory]
    [InlineData("1.0.0", "1.2.99-dev", true)]    // a "-dev" launcher build still satisfies the min (the 0-dev bug)
    [InlineData("1.0.0", "0.0.0-dev", false)]    // 0.0.0-dev parses (no throw) and is below min
    public void LauncherSupported_tolerates_prerelease(string minLauncher, string launcher, bool expected)
        => Assert.Equal(expected, VersionService.LauncherSupported(minLauncher, launcher));
}

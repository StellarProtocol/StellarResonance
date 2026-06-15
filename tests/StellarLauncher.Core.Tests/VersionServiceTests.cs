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
    public async Task Fetches_and_parses_manifest()
    {
        var json = """
        {"version":"1.2.0","date":"2026-06-08","bundleUrl":"u","sha256":"s",
         "minLauncherVersion":"1.0.0",
         "changelog":{"added":[],"changed":[],"fixed":[],"removed":[]}}
        """;
        var svc = new VersionService(new HttpClient(new StubHandler(json)));
        var m = await svc.FetchAsync(new Uri("https://x/version.json"));
        Assert.Equal("1.2.0", m.Version);
    }

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
}

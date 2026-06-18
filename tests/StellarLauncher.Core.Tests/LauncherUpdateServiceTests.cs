using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;
using Xunit;

public class LauncherUpdateServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        public StubHandler(string body) => _body = body;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_body) });
    }

    private const string Json = """
    {
      "version": "1.2.0",
      "date": "2026-06-08",
      "windowsUrl": "https://cdn.revette.io/launcher/StellarLauncher-win-x64.zip",
      "linuxUrl": "https://cdn.revette.io/launcher/StellarLauncher-linux-x64.zip",
      "notes": "faster startup"
    }
    """;

    [Fact]
    public async Task Fetches_and_parses_launcher_manifest()
    {
        var svc = new LauncherUpdateService(new HttpClient(new StubHandler(Json)));
        var m = await svc.FetchAsync(new Uri("https://cdn.revette.io/launcher.json"));
        Assert.Equal("1.2.0", m.Version);
        Assert.Equal("2026-06-08", m.Date);
        Assert.Equal("faster startup", m.Notes);
    }

    [Theory]
    [InlineData(true,  "https://cdn.revette.io/launcher/StellarLauncher-win-x64.zip")]
    [InlineData(false, "https://cdn.revette.io/launcher/StellarLauncher-linux-x64.zip")]
    public async Task DownloadUrlFor_picks_per_platform(bool isWindows, string expected)
    {
        var svc = new LauncherUpdateService(new HttpClient(new StubHandler(Json)));
        var m = await svc.FetchAsync(new Uri("https://x/launcher.json"));
        Assert.Equal(expected, m.DownloadUrlFor(isWindows));
    }
}

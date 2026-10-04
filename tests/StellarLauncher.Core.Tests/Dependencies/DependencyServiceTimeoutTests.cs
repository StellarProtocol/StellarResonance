using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Dependencies;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>Fix round 1, Important 1: HttpClient's own request timeout throws a TaskCanceledException
/// (a subtype of OperationCanceledException) that carries no relation to the CALLER's token — the
/// caller's own <c>ct</c> was never cancelled. EnsureOneAsync/EnsureAsync must only treat an
/// OperationCanceledException as a real cancellation when the caller's OWN ct says so; otherwise it is
/// exactly as unrelated to the ledger as any other download failure. Shares helpers/fields with
/// <see cref="DependencyServiceTests"/>.</summary>
public sealed partial class DependencyServiceTests
{
    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
    }

    [Fact]
    public async Task An_HttpClient_timeout_with_the_callers_token_uncancelled_becomes_Failed_download_timed_out()
    {
        var s = new DependencyService(_fs, new HttpClient(new TimeoutHandler()));
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");

        // CancellationToken.None: the caller never asked to cancel anything — only the HttpClient's own
        // internal timeout fired.
        var st = Assert.Single(await s.EnsureAsync(G, "p", new[] { d }, None, CancellationToken.None));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.Equal("download timed out", st.Detail);
        Assert.False(_fs.File.Exists("/game_mini/dxgi.dll"));
    }
}

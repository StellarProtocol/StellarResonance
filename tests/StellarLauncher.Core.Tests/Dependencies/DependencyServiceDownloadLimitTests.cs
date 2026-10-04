using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Dependencies;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>Final review I4 (a stalled download can't hold a launch forever), M-a (the registry's 512 MiB
/// ceiling), M-b (https only) and the launcher half of I5 (id charset; license, licenseUrl and sourceUrl
/// required). Shares helpers/fields with <see cref="DependencyServiceTests"/>.</summary>
public sealed partial class DependencyServiceTests
{
    /// <summary>A body that never delivers a byte — and ignores its cancellation token, the worst case.</summary>
    private sealed class NeverStream : Stream
    {
        public override Task<int> ReadAsync(byte[] b, int o, int c, CancellationToken ct) => new TaskCompletionSource<int>().Task;
        public override ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct = default) => new(new TaskCompletionSource<int>().Task);
        public override int Read(byte[] b, int o, int c) { Thread.Sleep(Timeout.Infinite); return 0; }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    /// <summary>A body that delivers one byte every 20 ms, forever: never inactive, never finished.</summary>
    private sealed class TrickleStream : Stream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct = default)
        {
            await Task.Delay(20, ct);
            b.Span[0] = 1;
            return 1;
        }
        public override Task<int> ReadAsync(byte[] b, int o, int c, CancellationToken ct) => ReadAsync(b.AsMemory(o, c), ct).AsTask();
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private sealed class BodyHandler(Func<Stream> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body()) });
    }

    [Fact]
    public async Task A_download_that_never_delivers_a_byte_times_out_after_the_inactivity_limit()
    {
        var s = new DependencyService(_fs, new HttpClient(new BodyHandler(() => new NeverStream())))
            { DownloadInactivityTimeout = TimeSpan.FromMilliseconds(200) };
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");

        // Bounded so the unfixed code fails here instead of hanging the suite.
        var st = Assert.Single(await s.EnsureAsync(G, "p", new[] { d }, None, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.Equal("download timed out", st.Detail);
        Assert.False(_fs.File.Exists("/game_mini/dxgi.dll"));
    }

    [Fact]
    public async Task A_download_that_trickles_forever_times_out_after_the_overall_limit()
    {
        var s = new DependencyService(_fs, new HttpClient(new BodyHandler(() => new TrickleStream())))
            { DownloadInactivityTimeout = TimeSpan.FromSeconds(5), DownloadOverallTimeout = TimeSpan.FromMilliseconds(300) };
        var d = File("fx", new byte[] { 1 }, "dxgi.dll") with { Size = 100_000_000 };

        var st = Assert.Single(await s.EnsureAsync(G, "p", new[] { d }, None, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.Equal("download timed out", st.Detail);
    }

    [Fact]
    public async Task The_callers_own_cancel_during_a_stalled_download_still_throws()
    {
        var s = new DependencyService(_fs, new HttpClient(new BodyHandler(() => new NeverStream())));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            s.EnsureAsync(G, "p", new[] { File("fx", new byte[] { 1 }, "dxgi.dll") }, None, cts.Token).WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void The_default_limits_are_30_seconds_idle_10_minutes_overall_and_512_MiB()
    {
        var s = new DependencyService(_fs, new HttpClient());
        Assert.Equal(TimeSpan.FromSeconds(30), s.DownloadInactivityTimeout);
        Assert.Equal(TimeSpan.FromMinutes(10), s.DownloadOverallTimeout);
        Assert.Equal(512L * 1024 * 1024, DependencyDeclaration.MaxBytes);
    }

    [Fact]
    public async Task A_declared_size_over_512_MiB_fails_without_downloading()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll") with { Size = DependencyDeclaration.MaxBytes + 1 };
        _downloads = 0;

        var st = Assert.Single(await Make().EnsureAsync(G, "p", new[] { d }, None, default));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.Equal("larger than the 512 MiB limit", st.Detail);
        Assert.Equal(0, _downloads);
    }

    [Fact]
    public async Task A_declared_size_of_exactly_512_MiB_is_accepted()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll") with { Size = DependencyDeclaration.MaxBytes };
        Assert.Equal(DependencyState.Installed, Assert.Single(await Make().EnsureAsync(G, "p", new[] { d }, None, default)).State);
    }

    [Theory]
    [InlineData("http://cdn/fx")]
    [InlineData("ftp://cdn/fx")]
    [InlineData("file:///etc/passwd")]
    [InlineData("cdn/fx")]
    public async Task A_non_https_download_url_is_refused_without_downloading(string url)
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");
        _web[url] = new byte[] { 1 };
        _downloads = 0;

        var st = Assert.Single(await Make().EnsureAsync(G, "p", new[] { d with { Url = url } }, None, default));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.Equal("download URL must be https", st.Detail);
        Assert.Equal(0, _downloads);
    }

    [Theory]
    [InlineData("", "https://l", "https://s", "no license declared")]
    [InlineData("MIT", null, "https://s", "no licenseUrl declared")]
    [InlineData("MIT", "https://l", " ", "no sourceUrl declared")]
    public async Task License_licenseUrl_and_sourceUrl_are_required(string license, string? licenseUrl, string? sourceUrl, string why)
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll") with { License = license, LicenseUrl = licenseUrl, SourceUrl = sourceUrl };
        _downloads = 0;
        var s = Make();

        var st = Assert.Single(await s.EnsureAsync(G, "p", new[] { d }, None, default));
        Assert.Equal(DependencyState.Failed, st.State);
        Assert.Equal(why, st.Detail);
        Assert.Equal(0, _downloads);
        Assert.Equal(st, Assert.Single(s.Status(G, "p", new[] { d }, None)));   // same answer read-only
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("fx beta")]
    [InlineData("")]
    public async Task A_dependency_id_outside_the_plugin_id_charset_is_refused(string id)
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll") with { Id = id };
        _downloads = 0;

        var st = Assert.Single(await Make().EnsureAsync(G, "p", new[] { d }, None, default));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.Equal("invalid dependency id", st.Detail);
        Assert.Equal(0, _downloads);
        Assert.False(_fs.File.Exists("/game_mini/dxgi.dll"));
    }
}

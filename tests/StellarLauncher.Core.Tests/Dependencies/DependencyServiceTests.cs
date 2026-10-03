using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

public sealed class DependencyServiceTests
{
    private const string G = "/game_mini";
    private readonly MockFileSystem _fs = new();
    private readonly Dictionary<string, byte[]> _web = new();
    private int _downloads;

    public DependencyServiceTests() => _fs.AddDirectory(G);

    private DependencyService Make() => new(_fs, new HttpClient(new Stub(this)));

    private sealed class Stub : HttpMessageHandler
    {
        private readonly DependencyServiceTests _t;
        public Stub(DependencyServiceTests t) => _t = t;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            _t._downloads++;
            return Task.FromResult(_t._web.TryGetValue(r.RequestUri!.ToString(), out var b)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(b) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private PluginDependency File(string id, byte[] bytes, string to, bool modded = false, bool optional = false, string[]? requires = null)
    {
        var url = $"https://cdn/{id}";
        _web[url] = bytes;
        return new PluginDependency(id, id, "1.0", url, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, "file",
            new[] { new PluginDependencyFile(null, to) }, "game", modded, optional, requires, "MIT");
    }

    private static readonly ISet<string> None = new HashSet<string>();

    [Fact]
    public async Task Installs_a_file_and_records_it_then_is_up_to_date_without_downloading()
    {
        var d = File("fx", new byte[] { 1, 2, 3 }, "dxgi.dll", modded: true);
        var s = Make();
        Assert.Equal(DependencyState.Installed, Assert.Single(await s.EnsureAsync(G, "p", new[] { d }, None, default)).State);
        Assert.Equal(new byte[] { 1, 2, 3 }, _fs.File.ReadAllBytes("/game_mini/dxgi.dll"));
        _downloads = 0;
        Assert.Equal(DependencyState.Installed, Assert.Single(await s.EnsureAsync(G, "p", new[] { d }, None, default)).State);
        Assert.Equal(0, _downloads);
    }

    [Fact]
    public async Task Checksum_mismatch_places_nothing()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll") with { Sha256 = new string('0', 64) };
        var st = Assert.Single(await Make().EnsureAsync(G, "p", new[] { d }, None, default));
        Assert.Equal(DependencyState.Failed, st.State);
        Assert.False(_fs.File.Exists("/game_mini/dxgi.dll"));
    }

    [Fact]
    public async Task A_foreign_file_blocks_and_is_never_overwritten()
    {
        _fs.AddFile("/game_mini/dxgi.dll", new MockFileData(new byte[] { 9 }));
        var st = Assert.Single(await Make().EnsureAsync(G, "p", new[] { File("fx", new byte[] { 1 }, "dxgi.dll") }, None, default));
        Assert.Equal(DependencyState.Blocked, st.State);
        Assert.Equal(new byte[] { 9 }, _fs.File.ReadAllBytes("/game_mini/dxgi.dll"));
    }

    [Fact]
    public async Task Skipped_removes_and_skips_dependents()
    {
        var a = File("a", new byte[] { 1 }, "a.dll", optional: true);
        var b = File("b", new byte[] { 2 }, "b.dll", requires: new[] { "a" });
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { a, b }, None, default);
        var st = await s.EnsureAsync(G, "p", new[] { a, b }, new HashSet<string> { "a" }, default);
        Assert.All(st, x => Assert.Equal(DependencyState.Skipped, x.State));
        Assert.False(_fs.File.Exists("/game_mini/a.dll"));
        Assert.False(_fs.File.Exists("/game_mini/b.dll"));
    }

    [Fact]
    public async Task Larger_than_declared_fails()
    {
        var d = File("fx", new byte[] { 1, 2, 3 }, "x.dll") with { Size = 2 };
        Assert.Equal(DependencyState.Failed, Assert.Single(await Make().EnsureAsync(G, "p", new[] { d }, None, default)).State);
    }

    [Fact]
    public async Task Zip_prefix_maps_entries_and_refuses_escape()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("pack/Shaders/a.fx").Open())) w.Write("A");
            using (var w = new StreamWriter(zip.CreateEntry("pack/README").Open())) w.Write("R");
        }
        var bytes = ms.ToArray();
        _web["https://cdn/z"] = bytes;
        var d = new PluginDependency("z", "z", "1", "https://cdn/z", Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, "zip",
            new[] { new PluginDependencyFile("pack/Shaders/", "fx/Shaders/") }, "plugin");
        Assert.Equal(DependencyState.Installed, Assert.Single(await Make().EnsureAsync(G, "p", new[] { d }, None, default)).State);
        Assert.Equal("A", _fs.File.ReadAllText("/game_mini/stellar/deps/p/fx/Shaders/a.fx"));
        Assert.False(_fs.File.Exists("/game_mini/stellar/deps/p/fx/README"));
    }

    [Fact]
    public async Task RemoveAll_deletes_exactly_what_was_placed()
    {
        _fs.AddFile("/game_mini/keep.txt", new MockFileData("k"));
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { File("fx", new byte[] { 1 }, "dxgi.dll") }, None, default);
        s.RemoveAll(G, "p");
        Assert.False(_fs.File.Exists("/game_mini/dxgi.dll"));
        Assert.True(_fs.File.Exists("/game_mini/keep.txt"));
        Assert.False(_fs.File.Exists("/game_mini/stellar/deps/p.json"));
    }
}

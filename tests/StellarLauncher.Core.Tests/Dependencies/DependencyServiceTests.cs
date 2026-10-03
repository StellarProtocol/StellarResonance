using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
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

public sealed partial class DependencyServiceTests
{
    private const string G = "/game_mini";
    private readonly MockFileSystem _fs = new();
    private readonly Dictionary<string, byte[]> _web = new();
    private int _downloads;

    public DependencyServiceTests() => _fs.AddDirectory(G);

    private DependencyService Make() => new(_fs, new HttpClient(new Stub(this)));
    private static DependencyService MakeWith(IFileSystem fs) => new(fs, new HttpClient());

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
        await s.RemoveAllAsync(G, "p");
        Assert.False(_fs.File.Exists("/game_mini/dxgi.dll"));
        Assert.True(_fs.File.Exists("/game_mini/keep.txt"));
        Assert.False(_fs.File.Exists("/game_mini/stellar/deps/p.json"));
    }

    // I3: Remove must not delete a file the user or another tool has since replaced — only drop the
    // now-untrustworthy ledger entry.
    [Fact]
    public async Task Remove_leaves_a_user_replaced_file_alone_but_drops_the_ledger_entry()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { d }, None, default);
        _fs.File.WriteAllBytes("/game_mini/dxgi.dll", new byte[] { 42 });

        await s.RemoveAsync(G, "p", "fx");

        Assert.Equal(new byte[] { 42 }, _fs.File.ReadAllBytes("/game_mini/dxgi.dll"));
        Assert.Empty(new DependencyLedgerStore(_fs).Read(G, "p").Entries);
    }

    // I4: ownership is per (plugin, dependency) — a second PLUGIN targeting a file another plugin already
    // owns is Blocked, and removing the blocked plugin's (empty) ledger never touches the owner's file.
    [Fact]
    public async Task A_different_plugin_targeting_an_owned_file_is_blocked_and_the_owner_survives_its_RemoveAll()
    {
        var depA = File("fx", new byte[] { 1 }, "shared.dll");
        var s = Make();
        await s.EnsureAsync(G, "pluginA", new[] { depA }, None, default);

        var depB = File("fx", new byte[] { 9 }, "shared.dll");
        var stB = Assert.Single(await s.EnsureAsync(G, "pluginB", new[] { depB }, None, default));
        Assert.Equal(DependencyState.Blocked, stB.State);
        Assert.Equal(new byte[] { 1 }, _fs.File.ReadAllBytes("/game_mini/shared.dll"));

        await s.RemoveAllAsync(G, "pluginB");
        Assert.True(_fs.File.Exists("/game_mini/shared.dll"));
        Assert.Equal(new byte[] { 1 }, _fs.File.ReadAllBytes("/game_mini/shared.dll"));
    }

    // I4: ownership is also scoped per dependency id within the SAME plugin's ledger.
    [Fact]
    public async Task A_different_dependency_id_in_the_same_plugin_targeting_an_owned_file_is_blocked()
    {
        var depA = File("fxA", new byte[] { 1 }, "shared.dll");
        var depB = File("fxB", new byte[] { 2 }, "shared.dll");
        var st = await Make().EnsureAsync(G, "p", new[] { depA, depB }, None, default);
        Assert.Equal(DependencyState.Installed, st[0].State);
        Assert.Equal(DependencyState.Blocked, st[1].State);
        Assert.Equal(new byte[] { 1 }, _fs.File.ReadAllBytes("/game_mini/shared.dll"));
    }

    // M11: ownership/foreign matching is ordinal (case-sensitive) — a ledger entry recorded for
    // "Shared.dll" must not be treated as already owning a differently-cased "shared.dll", even on a
    // version bump of the very same dependency id, so a genuinely foreign lowercase file is never silently
    // overwritten.
    [Fact]
    public async Task Ownership_matching_is_case_sensitive()
    {
        var v1 = File("fx", new byte[] { 1 }, "Shared.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { v1 }, None, default); // ledger records "Shared.dll"
        _fs.AddFile("/game_mini/shared.dll", new MockFileData(new byte[] { 99 })); // unrelated, foreign file

        var v2 = File("fx", new byte[] { 2 }, "shared.dll") with { Version = "2.0" };
        var st = Assert.Single(await s.EnsureAsync(G, "p", new[] { v2 }, None, default));

        Assert.Equal(DependencyState.Blocked, st.State);
        Assert.Equal(new byte[] { 99 }, _fs.File.ReadAllBytes("/game_mini/shared.dll"));
    }

    // I2: an unsafe recorded path must never be resolved to an absolute path, so Remove/Park/Unpark can
    // never act on it — a file planted at the would-be escape target is left untouched either way.
    [Fact]
    public async Task Remove_park_and_unpark_all_ignore_unsafe_ledger_paths()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(G);
        fs.AddFile("/x", new MockFileData("outside-x"));
        fs.AddFile("/abs/x", new MockFileData("outside-abs"));
        new DependencyLedgerStore(fs).Write(G, new DependencyLedger("p", new[] { new LedgerEntry("evil", "1", new[]
        {
            new LedgerFile("../x", "h1", true),
            new LedgerFile("/abs/x", "h2", true),
            new LedgerFile("C:/x", "h3", true),
        }) }));
        var s = MakeWith(fs);

        await s.ParkModdedOnlyAsync(G);
        await s.UnparkModdedOnlyAsync(G);
        Assert.Equal("outside-x", fs.File.ReadAllText("/x"));
        Assert.Equal("outside-abs", fs.File.ReadAllText("/abs/x"));

        await s.RemoveAsync(G, "p", "evil");
        Assert.Equal("outside-x", fs.File.ReadAllText("/x"));
        Assert.Equal("outside-abs", fs.File.ReadAllText("/abs/x"));
        Assert.Empty(new DependencyLedgerStore(fs).Read(G, "p").Entries);
    }
}

using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>C1 (transactional placement), M12 (temp-file cleanup) and I6 (skip-removal IO failure) from
/// the Task 4 review round. Shares helpers/fields with <see cref="DependencyServiceTests"/>.</summary>
public sealed partial class DependencyServiceTests
{
    // C1: a failure partway through WriteAll must roll back everything THIS attempt created (so the
    // ledger and disk agree — nothing untracked is left behind) and must not write the ledger at all; a
    // retry (the transient fault gone) must then succeed as Installed, never self-Blocked by its own debris.
    [Fact]
    public async Task Failure_during_placement_rolls_back_created_files_and_a_retry_succeeds()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("pack/a.fx").Open())) w.Write("A");
            using (var w = new StreamWriter(zip.CreateEntry("pack/b.fx").Open())) w.Write("B");
            using (var w = new StreamWriter(zip.CreateEntry("pack/c.fx").Open())) w.Write("C");
        }
        var bytes = ms.ToArray();
        _web["https://cdn/z3"] = bytes;
        var d = new PluginDependency("z3", "z3", "1", "https://cdn/z3", Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, "zip",
            new[] { new PluginDependencyFile("pack/", "out/") }, "plugin");

        const string bPath = "/game_mini/stellar/deps/p/out/b.fx";
        var faulty = new FaultInjectingFileSystem(_fs, bPath, "Move"); // fails the 2nd of 3 writes, once
        var s = new DependencyService(faulty, new HttpClient(new Stub(this)));

        var st1 = Assert.Single(await s.EnsureAsync(G, "p", new[] { d }, None, default));
        Assert.Equal(DependencyState.Failed, st1.State);
        Assert.False(_fs.File.Exists("/game_mini/stellar/deps/p/out/a.fx"));   // rolled back — didn't exist before
        Assert.False(_fs.File.Exists(bPath));                                  // Move never completed it
        Assert.False(_fs.File.Exists("/game_mini/stellar/deps/p/out/c.fx"));   // never attempted
        Assert.False(_fs.File.Exists(bPath + ".stellar-tmp"));                 // no leftover temp file
        Assert.False(_fs.File.Exists("/game_mini/stellar/deps/p.json"));       // ledger written only at the end

        var st2 = Assert.Single(await s.EnsureAsync(G, "p", new[] { d }, None, default));
        Assert.Equal(DependencyState.Installed, st2.State); // not Blocked by leftover debris from the first attempt
        Assert.Equal("A", _fs.File.ReadAllText("/game_mini/stellar/deps/p/out/a.fx"));
        Assert.Equal("B", _fs.File.ReadAllText(bPath));
        Assert.Equal("C", _fs.File.ReadAllText("/game_mini/stellar/deps/p/out/c.fx"));
    }

    // M12: a stale .stellar-tmp left by a previous crashed attempt must never survive a fresh, successful install.
    [Fact]
    public async Task Stale_leftover_tmp_file_is_cleaned_up_and_does_not_block_a_fresh_install()
    {
        var d = File("fx", new byte[] { 1, 2, 3 }, "dxgi.dll");
        _fs.AddFile("/game_mini/dxgi.dll.stellar-tmp", new MockFileData(new byte[] { 0xDE, 0xAD }));

        var st = Assert.Single(await Make().EnsureAsync(G, "p", new[] { d }, None, default));

        Assert.Equal(DependencyState.Installed, st.State);
        Assert.Equal(new byte[] { 1, 2, 3 }, _fs.File.ReadAllBytes("/game_mini/dxgi.dll"));
        Assert.False(_fs.File.Exists("/game_mini/dxgi.dll.stellar-tmp"));
    }

    // M8: rewriting a destination (e.g. a version bump) must drop any stale parked copy for it — otherwise
    // a later Unpark would restore outdated bytes over the freshly written live file.
    [Fact]
    public async Task Rewriting_a_destination_drops_its_stale_parked_copy()
    {
        var v1 = File("fx", new byte[] { 1 }, "dxgi.dll", modded: true);
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { v1 }, None, default);
        s.ParkModdedOnly(G); // moves the v1 bytes into deps-parked/
        Assert.True(_fs.File.Exists("/game_mini/stellar/deps-parked/p/dxgi.dll"));

        var v2 = File("fx", new byte[] { 2 }, "dxgi.dll", modded: true) with { Version = "2.0" };
        await s.EnsureAsync(G, "p", new[] { v2 }, None, default); // live rewrite while the parked copy is stale

        Assert.Equal(new byte[] { 2 }, _fs.File.ReadAllBytes("/game_mini/dxgi.dll"));
        Assert.False(_fs.File.Exists("/game_mini/stellar/deps-parked/p/dxgi.dll")); // stale copy dropped

        s.UnparkModdedOnly(G); // nothing left to (wrongly) restore
        Assert.Equal(new byte[] { 2 }, _fs.File.ReadAllBytes("/game_mini/dxgi.dll"));
    }

    // I6: the skip branch's own Remove can fail on IO too — that must come back as Failed, never throw out of EnsureAsync.
    [Fact]
    public async Task Skip_removal_IO_failure_yields_Failed_not_a_thrown_exception()
    {
        var a = File("a", new byte[] { 1 }, "a.dll");
        var faulty = new FaultInjectingFileSystem(_fs, "/game_mini/a.dll", "Delete");
        var s = new DependencyService(faulty, new HttpClient(new Stub(this)));
        await s.EnsureAsync(G, "p", new[] { a }, None, default); // installs normally — Delete not yet touched

        var ex = await Record.ExceptionAsync(async () =>
        {
            var st = Assert.Single(await s.EnsureAsync(G, "p", new[] { a }, new System.Collections.Generic.HashSet<string> { "a" }, default));
            Assert.Equal(DependencyState.Failed, st.State);
        });
        Assert.Null(ex);
    }
}

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>I1 (backup-and-restore on a failed update) and its minor 4 (a rollback failure must not mask
/// the original error), from the Task 4 fix-round-2 review. Shares helpers/fields with
/// <see cref="DependencyServiceTests"/>.</summary>
public sealed partial class DependencyServiceTests
{
    private const string UpdPath = "/game_mini/stellar/deps/p/out/";

    private (PluginDependency V1, PluginDependency V2, byte[] V1Bytes, byte[] V2Bytes) BuildUpdatePair()
    {
        var v1Bytes = ZipOf(("pack/a.fx", "A1"), ("pack/b.fx", "B1"), ("pack/c.fx", "C1"));
        var v2Bytes = ZipOf(("pack/a.fx", "A2"), ("pack/b.fx", "B2"), ("pack/c.fx", "C2"));
        var v1 = new PluginDependency("upd", "upd", "1", "https://cdn/upd", Convert.ToHexString(SHA256.HashData(v1Bytes)), v1Bytes.Length, "zip",
            new[] { new PluginDependencyFile("pack/", "out/") }, "plugin");
        var v2 = new PluginDependency("upd", "upd", "2", "https://cdn/upd", Convert.ToHexString(SHA256.HashData(v2Bytes)), v2Bytes.Length, "zip",
            new[] { new PluginDependencyFile("pack/", "out/") }, "plugin");
        return (v1, v2, v1Bytes, v2Bytes);
    }

    private static byte[] ZipOf(params (string Name, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (name, content) in entries)
                using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write(content);
        return ms.ToArray();
    }

    // I1: a failed UPDATE must leave v1 intact on every path it touched, with no backup/temp debris and
    // the ledger still pointing at v1.
    [Fact]
    public async Task Failed_update_restores_v1_on_every_path_and_leaves_no_backup_debris()
    {
        var (v1, v2, v1Bytes, v2Bytes) = BuildUpdatePair();
        _web["https://cdn/upd"] = v1Bytes;
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { v1 }, None, default);

        _web["https://cdn/upd"] = v2Bytes;
        var bPath = UpdPath + "b.fx";
        var faulty = new FaultInjectingFileSystem(_fs, bPath, "Move"); // fails the 2nd file's content-write, once
        var s2 = new DependencyService(faulty, new HttpClient(new Stub(this)));

        var st = Assert.Single(await s2.EnsureAsync(G, "p", new[] { v2 }, None, default));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.Equal("A1", _fs.File.ReadAllText(UpdPath + "a.fx"));
        Assert.Equal("B1", _fs.File.ReadAllText(bPath));
        Assert.Equal("C1", _fs.File.ReadAllText(UpdPath + "c.fx"));
        Assert.Equal("1", new DependencyLedgerStore(_fs).Read(G, "p").Entries.Single(e => e.DependencyId == "upd").Version);
        Assert.False(_fs.File.Exists(UpdPath + "a.fx.stellar-bak"));
        Assert.False(_fs.File.Exists(bPath + ".stellar-bak"));
        Assert.False(_fs.File.Exists(UpdPath + "c.fx.stellar-bak"));
        Assert.False(_fs.File.Exists(bPath + ".stellar-tmp"));
    }

    // Minor 4: if restoring a backup during rollback itself throws, the ORIGINAL failure (the one that
    // triggered the rollback) must still be what's reported — never the rollback's own exception.
    [Fact]
    public async Task Rollback_failure_does_not_mask_the_original_error()
    {
        var (v1, v2, v1Bytes, v2Bytes) = BuildUpdatePair();
        _web["https://cdn/upd"] = v1Bytes;
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { v1 }, None, default);

        _web["https://cdn/upd"] = v2Bytes;
        var bPath = UpdPath + "b.fx";
        // Fails EVERY Move to b.fx: first its content-write (the original cause), then its own
        // rollback-restore (which must not override the reported reason).
        var faulty = new FaultInjectingFileSystem(_fs, bPath, once: false, "Move");
        var s2 = new DependencyService(faulty, new HttpClient(new Stub(this)));

        var st = Assert.Single(await s2.EnsureAsync(G, "p", new[] { v2 }, None, default));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.Equal($"injected failure #1: Move {bPath}", st.Detail);
    }
}

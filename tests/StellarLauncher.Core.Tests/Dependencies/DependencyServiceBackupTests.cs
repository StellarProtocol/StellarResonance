using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
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
            new[] { new PluginDependencyFile("pack/", "out/") }, "plugin", License: "MIT", LicenseUrl: "https://l", SourceUrl: "https://s");
        var v2 = new PluginDependency("upd", "upd", "2", "https://cdn/upd", Convert.ToHexString(SHA256.HashData(v2Bytes)), v2Bytes.Length, "zip",
            new[] { new PluginDependencyFile("pack/", "out/") }, "plugin", License: "MIT", LicenseUrl: "https://l", SourceUrl: "https://s");
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
        Assert.DoesNotContain("(rollback incomplete)", st.Detail); // a fully-successful rollback never gets the suffix
    }

    // Round-2 minor 4: if restoring a backup during rollback itself throws, the ORIGINAL failure (the one
    // that triggered the rollback) must still be what's reported — never the rollback's own exception.
    // Round-3 minor 4: since that restore genuinely failed, "(rollback incomplete)" is appended too.
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
        Assert.Equal($"injected failure #1: Move {bPath} (rollback incomplete)", st.Detail);
    }

    // Minor 1: RestoreBackups must restore only the paths THIS attempt backed up — a stale .stellar-bak
    // left by some unrelated past event must never be used to "restore" a freshly created file.
    [Fact]
    public async Task Rollback_never_restores_a_stale_backup_this_attempt_did_not_create()
    {
        var bytes = ZipOf(("pack/a.fx", "A"), ("pack/b.fx", "B"), ("pack/c.fx", "C"));
        _web["https://cdn/fresh"] = bytes;
        var d = new PluginDependency("fresh", "fresh", "1", "https://cdn/fresh", Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, "zip",
            new[] { new PluginDependencyFile("pack/", "out/") }, "plugin", License: "MIT", LicenseUrl: "https://l", SourceUrl: "https://s");

        var aPath = UpdPath + "a.fx";
        var bPath = UpdPath + "b.fx";
        _fs.AddFile(aPath + ".stellar-bak", new MockFileData("STALE")); // debris from some unrelated past event
        var faulty = new FaultInjectingFileSystem(_fs, bPath, "Move");
        var s2 = new DependencyService(faulty, new HttpClient(new Stub(this)));

        var st = Assert.Single(await s2.EnsureAsync(G, "p", new[] { d }, None, default));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.False(_fs.File.Exists(aPath)); // deleted (newly created this attempt) — never "restored" from stale debris
    }

    // Minor 2: a restore failure for one file must not skip the restores of the OTHER files in the same
    // dependency. Minor 4: that leaves the rollback incomplete, noted on the Failed detail.
    [Fact]
    public async Task Rollback_isolates_failures_per_file_so_other_restores_still_happen()
    {
        var (v1, v2, v1Bytes, v2Bytes) = BuildUpdatePair();
        _web["https://cdn/upd"] = v1Bytes;
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { v1 }, None, default);

        _web["https://cdn/upd"] = v2Bytes;
        var aPath = UpdPath + "a.fx";
        var cPath = UpdPath + "c.fx";
        // "a"'s restore (the 2nd Move to a.fx) fails; "c"'s content-write (the 1st Move to c.fx) fails and
        // triggers the whole rollback.
        var inner = new FaultInjectingFileSystem(_fs, aPath, once: true, afterCalls: 1, "Move");
        var faulty = new FaultInjectingFileSystem(inner, cPath, "Move");
        var s2 = new DependencyService(faulty, new HttpClient(new Stub(this)));

        var st = Assert.Single(await s2.EnsureAsync(G, "p", new[] { v2 }, None, default));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.Equal("A2", _fs.File.ReadAllText(aPath));               // its own restore failed — left at the new content
        Assert.Equal("B1", _fs.File.ReadAllText(UpdPath + "b.fx"));    // restored despite a's failure
        Assert.Equal("C1", _fs.File.ReadAllText(cPath));               // restored despite a's failure
        Assert.Contains("(rollback incomplete)", st.Detail);
    }

    // Minor 3: a destination's parked copy must only be deleted once the ledger write actually commits —
    // never when the attempt as a whole failed, even if the file content itself was already rolled back.
    [Fact]
    public async Task Parked_copy_cleanup_is_deferred_until_the_ledger_write_succeeds()
    {
        var d1 = File("fx", new byte[] { 1 }, "dxgi.dll", modded: true);
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { d1 }, None, default);
        _fs.AddFile("/game_mini/stellar/deps-parked/p/dxgi.dll", new MockFileData(new byte[] { 0xAA })); // stale leftover

        var d2 = File("fx", new byte[] { 2 }, "dxgi.dll", modded: true) with { Version = "2.0" };
        var ledgerPath = "/game_mini/stellar/deps/p.json";
        var faulty = new FaultInjectingFileSystem(_fs, ledgerPath, "Move"); // fails the ledger's atomic rename
        var s2 = new DependencyService(faulty, new HttpClient(new Stub(this)));

        var st = Assert.Single(await s2.EnsureAsync(G, "p", new[] { d2 }, None, default));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.True(_fs.File.Exists("/game_mini/stellar/deps-parked/p/dxgi.dll")); // never deleted — the write never committed
        Assert.Equal(new byte[] { 0xAA }, _fs.File.ReadAllBytes("/game_mini/stellar/deps-parked/p/dxgi.dll"));
    }

    // Minor 2 (round 4): a stale .stellar-bak must be cleared even when the destination is currently
    // absent (nothing to back up this time) — otherwise it lingers as debris indefinitely.
    [Fact]
    public async Task A_stale_backup_is_cleared_even_when_the_destination_is_absent()
    {
        _fs.AddFile("/game_mini/fresh.dll.stellar-bak", new MockFileData("STALE"));
        var d = File("fx", new byte[] { 9 }, "fresh.dll");

        var st = Assert.Single(await Make().EnsureAsync(G, "p", new[] { d }, None, default));

        Assert.Equal(DependencyState.Installed, st.State);
        Assert.Equal(new byte[] { 9 }, _fs.File.ReadAllBytes("/game_mini/fresh.dll"));
        Assert.False(_fs.File.Exists("/game_mini/fresh.dll.stellar-bak")); // cleaned up even though nothing was backed up
    }
}

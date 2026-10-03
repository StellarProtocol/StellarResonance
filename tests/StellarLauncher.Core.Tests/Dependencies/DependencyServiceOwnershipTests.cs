using System.IO.Abstractions.TestingHelpers;
using System.Net.Http;
using StellarLauncher.Core.Dependencies;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>I2 (a modified owned file is never overwritten) and M1 (ownership is checked whether or not
/// the destination exists) from the Task 4 fix-round-2 review. Shares helpers/fields with
/// <see cref="DependencyServiceTests"/>.</summary>
public sealed partial class DependencyServiceTests
{
    // I2: a launcher-placed file the user edited afterward is "the player's file" — Ensure must not
    // overwrite it, must report Blocked "<path> (modified)", and must drop it from the ledger so it is
    // never touched again (future attempts treat it as an ordinary foreign file).
    [Fact]
    public async Task A_user_modified_owned_file_blocks_as_modified_and_is_dropped_from_the_ledger()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { d }, None, default);
        _fs.File.WriteAllBytes("/game_mini/dxgi.dll", new byte[] { 42 }); // the user replaces it

        var d2 = File("fx", new byte[] { 2 }, "dxgi.dll") with { Version = "2.0" };
        var st = Assert.Single(await s.EnsureAsync(G, "p", new[] { d2 }, None, default));

        Assert.Equal(DependencyState.Blocked, st.State);
        Assert.Equal("dxgi.dll (modified)", st.Detail);
        Assert.Equal(new byte[] { 42 }, _fs.File.ReadAllBytes("/game_mini/dxgi.dll"));
        Assert.Empty(new DependencyLedgerStore(_fs).Read(G, "p").Entries);
    }

    // M1: ownership is checked regardless of presence — a path another plugin's ledger records, even
    // while currently parked away (so nothing physically sits there right now), is still foreign.
    [Fact]
    public async Task A_parked_file_owned_by_another_plugin_is_still_foreign()
    {
        var qDep = File("fx", new byte[] { 1 }, "dxgi.dll", modded: true);
        var s = Make();
        await s.EnsureAsync(G, "q", new[] { qDep }, None, default);
        s.ParkModdedOnly(G); // the destination no longer physically exists

        var pDep = File("fx2", new byte[] { 2 }, "dxgi.dll");
        var st = Assert.Single(await s.EnsureAsync(G, "p", new[] { pDep }, None, default));

        Assert.Equal(DependencyState.Blocked, st.State);
        Assert.False(_fs.File.Exists("/game_mini/dxgi.dll"));
        Assert.True(_fs.File.Exists("/game_mini/stellar/deps-parked/q/dxgi.dll"));
    }

    // Minor 1 (round 4): an earlier attempt's INCOMPLETE rollback can leave the live file holding newer
    // bytes than the ledger while its .stellar-bak still holds exactly what the ledger expects — that's
    // our own half-finished repair, not the player's edit, so a later attempt must overwrite it rather
    // than block it as "(modified)" (which would also wrongly drop it from the ledger forever).
    [Fact]
    public async Task A_file_left_mid_incomplete_rollback_is_treated_as_ours_not_modified()
    {
        var d1 = File("fx", new byte[] { 1 }, "dxgi.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { d1 }, None, default); // v1 installed; ledger records hash({1})

        // Simulate the aftermath of a rollback whose own restore failed: live file already holds v2 bytes,
        // but a backup matching the ledger's recorded (v1) hash is still sitting there.
        _fs.File.WriteAllBytes("/game_mini/dxgi.dll", new byte[] { 2 });
        _fs.AddFile("/game_mini/dxgi.dll.stellar-bak", new MockFileData(new byte[] { 1 }));

        var d2 = File("fx", new byte[] { 2 }, "dxgi.dll") with { Version = "2.0" };
        var st = Assert.Single(await s.EnsureAsync(G, "p", new[] { d2 }, None, default));

        Assert.Equal(DependencyState.Installed, st.State); // not Blocked "(modified)"
        Assert.Equal(new byte[] { 2 }, _fs.File.ReadAllBytes("/game_mini/dxgi.dll"));
        Assert.Single(new DependencyLedgerStore(_fs).Read(G, "p").Entries); // still tracked, not dropped
    }

    // Minor 2 (round 6): the mid-repair restore must happen BEFORE the normal backup-and-replace cycle —
    // otherwise WriteAll's own fresh backup would overwrite the last known-good copy, so a SECOND,
    // independent failure during this very retry would orphan the file for good instead of being
    // restorable back to the TRUE original.
    [Fact]
    public async Task Mid_repair_restore_happens_before_the_normal_backup_so_a_second_failure_cannot_orphan_it()
    {
        var d1 = File("fx", new byte[] { 1 }, "dxgi.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { d1 }, None, default); // v1 installed; ledger records hash({1})

        // Aftermath of an incomplete rollback: live file holds v2 bytes ("2"), backup holds the TRUE
        // original v1 bytes ("1") matching the ledger.
        _fs.File.WriteAllBytes("/game_mini/dxgi.dll", new byte[] { 2 });
        _fs.AddFile("/game_mini/dxgi.dll.stellar-bak", new MockFileData(new byte[] { 1 }));

        // This retry's OWN backup-and-replace step fails too — a second, independent failure.
        var faulty = new FaultInjectingFileSystem(_fs, "/game_mini/dxgi.dll.stellar-bak", "Move");
        var s2 = new DependencyService(faulty, new HttpClient(new Stub(this)));
        var d2 = File("fx", new byte[] { 3 }, "dxgi.dll") with { Version = "2.0" };

        var st = Assert.Single(await s2.EnsureAsync(G, "p", new[] { d2 }, None, default));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.Equal(new byte[] { 1 }, _fs.File.ReadAllBytes("/game_mini/dxgi.dll")); // restored to the TRUE original — never orphaned
        Assert.False(_fs.File.Exists("/game_mini/dxgi.dll.stellar-bak"));
    }
}

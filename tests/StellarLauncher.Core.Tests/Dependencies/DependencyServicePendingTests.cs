using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using StellarLauncher.Core.Dependencies;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>Final review I2: a crash between placing a dependency's files and committing its ledger entry must
/// not turn the launcher's own file into "the player's file" (Blocked forever). The pending record written
/// before the first move is what tells the two apart — and nothing else does: a player's byte-identical file
/// with no pending record stays theirs. Shares helpers/fields with <see cref="DependencyServiceTests"/>.</summary>
public sealed partial class DependencyServiceTests
{
    [Fact]
    public async Task A_kill_between_placing_and_the_ledger_write_is_Installed_on_the_next_run_not_Blocked()
    {
        var d = File("fx", new byte[] { 1, 2, 3 }, "dxgi.dll", modded: true);
        var killFs = new KillSwitchFileSystem(_fs, "/game_mini/dxgi.dll");

        var first = Assert.Single(await new DependencyService(killFs, new HttpClient(new Stub(this))).EnsureAsync(G, "p", new[] { d }, None, default));
        Assert.True(killFs.Dead);                                   // the "process" died right after the move
        Assert.NotEqual(DependencyState.Installed, first.State);    // …so this run never committed
        Assert.Equal(new byte[] { 1, 2, 3 }, _fs.File.ReadAllBytes("/game_mini/dxgi.dll")); // our file is on disk

        // Relaunch: a fresh service over the disk the dead process left behind.
        var st = Assert.Single(await Make().EnsureAsync(G, "p", new[] { d }, None, default));
        Assert.Equal(DependencyState.Installed, st.State);
        var ledger = new DependencyLedgerStore(_fs).Read(G, "p");
        Assert.Equal("dxgi.dll", Assert.Single(Assert.Single(ledger.Entries).Files).Path);
        Assert.Null(ledger.Pending);                                 // committed: nothing left in flight
    }

    [Fact]
    public async Task Status_reads_a_killed_installs_own_file_as_not_installed_not_Blocked()
    {
        var d = File("fx", new byte[] { 1, 2, 3 }, "dxgi.dll");
        await new DependencyService(new KillSwitchFileSystem(_fs, "/game_mini/dxgi.dll"), new HttpClient(new Stub(this)))
            .EnsureAsync(G, "p", new[] { d }, None, default);

        Assert.Equal(DependencyState.NotInstalled, Assert.Single(Make().Status(G, "p", new[] { d }, None)).State);
    }

    [Fact]
    public async Task A_players_byte_identical_file_with_no_pending_record_is_Blocked_and_never_adopted()
    {
        var bytes = new byte[] { 1, 2, 3 };
        _fs.AddFile("/game_mini/dxgi.dll", new MockFileData(bytes));   // the player's own copy, same bytes
        var d = File("fx", bytes, "dxgi.dll");

        var st = Assert.Single(await Make().EnsureAsync(G, "p", new[] { d }, None, default));
        Assert.Equal(DependencyState.Blocked, st.State);
        Assert.Empty(new DependencyLedgerStore(_fs).Read(G, "p").Entries);
    }

    [Fact]
    public async Task A_pending_record_of_another_dependency_never_adopts_a_file()
    {
        var bytes = new byte[] { 1, 2, 3 };
        _fs.AddFile("/game_mini/dxgi.dll", new MockFileData(bytes));
        new DependencyLedgerStore(_fs).Write(G, new DependencyLedger("p", Array.Empty<LedgerEntry>(),
            new[] { new LedgerEntry("other", "1", new[] { new LedgerFile("dxgi.dll", Convert.ToHexString(SHA256.HashData(bytes)), false) }) }));

        // "other" stays declared (so the undeclared-entry sweep leaves its record alone) and is checked AFTER fx.
        var st = await Make().EnsureAsync(G, "p", new[] { File("fx", bytes, "dxgi.dll"), File("other", new byte[] { 5 }, "o.dll") }, None, default);
        Assert.Equal(DependencyState.Blocked, st[0].State);
        Assert.Equal(bytes, _fs.File.ReadAllBytes("/game_mini/dxgi.dll"));
    }

    [Fact]
    public async Task A_pending_file_the_player_since_replaced_is_Blocked_and_kept()
    {
        var d = File("fx", new byte[] { 1, 2, 3 }, "dxgi.dll");
        await new DependencyService(new KillSwitchFileSystem(_fs, "/game_mini/dxgi.dll"), new HttpClient(new Stub(this)))
            .EnsureAsync(G, "p", new[] { d }, None, default);
        _fs.File.WriteAllBytes("/game_mini/dxgi.dll", new byte[] { 9 });   // the player put their own file there

        Assert.Equal(DependencyState.Blocked, Assert.Single(await Make().EnsureAsync(G, "p", new[] { d }, None, default)).State);
        Assert.Equal(new byte[] { 9 }, _fs.File.ReadAllBytes("/game_mini/dxgi.dll"));
    }

    [Fact]
    public async Task The_pending_record_is_on_disk_before_the_first_file_is_moved_into_place()
    {
        var d = File("fx", new byte[] { 1, 2, 3 }, "dxgi.dll");
        await new DependencyService(new KillSwitchFileSystem(_fs, "/game_mini/dxgi.dll"), new HttpClient(new Stub(this)))
            .EnsureAsync(G, "p", new[] { d }, None, default);

        var pending = new DependencyLedgerStore(_fs).Read(G, "p").Pending;
        var f = Assert.Single(Assert.Single(pending!).Files);
        Assert.Equal("dxgi.dll", f.Path);
        Assert.Equal(d.Sha256, f.Sha256, ignoreCase: true);
    }

    [Fact]
    public async Task RemoveAll_also_deletes_a_killed_installs_own_files_and_the_pending_record()
    {
        var d = File("fx", new byte[] { 1, 2, 3 }, "dxgi.dll");
        await new DependencyService(new KillSwitchFileSystem(_fs, "/game_mini/dxgi.dll"), new HttpClient(new Stub(this)))
            .EnsureAsync(G, "p", new[] { d }, None, default);

        await Make().RemoveAllAsync(G, "p");
        Assert.False(_fs.File.Exists("/game_mini/dxgi.dll"));
        Assert.False(_fs.File.Exists("/game_mini/stellar/deps/p.json"));
    }

    [Fact]
    public async Task A_successful_install_leaves_no_pending_record_and_an_old_ledger_without_one_still_reads()
    {
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { File("fx", new byte[] { 1 }, "a.dll") }, None, default);
        Assert.DoesNotContain("Pending", _fs.File.ReadAllText("/game_mini/stellar/deps/p.json"));
        Assert.Null(new DependencyLedgerStore(_fs).Read(G, "p").Pending);
    }
}

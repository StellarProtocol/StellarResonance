using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using StellarLauncher.Core.Dependencies;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>v3 V2/V3 (spec § 12): a plugin removed with "only" keeps its dependencies under launcher management (ledger
/// marked kept); an ensure after a reinstall adopts them again; a requested reinstall re-downloads and re-verifies every
/// used dependency without ever overwriting the player's own file.</summary>
public sealed partial class DependencyServiceTests
{
    private DependencyLedger LedgerOf(string pluginId = "p") => new DependencyLedgerStore(_fs).Read(G, pluginId, quarantine: false);

    [Fact]
    public async Task SetKept_marks_the_ledger_and_touches_no_file()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll", modded: true);
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { d }, None, default);

        await s.SetKeptAsync(G, "p", true);

        Assert.True(s.IsKept(G, "p"));
        Assert.True(LedgerOf().Kept);
        Assert.Equal(new byte[] { 1 }, _fs.File.ReadAllBytes("/game_mini/dxgi.dll"));
        Assert.Equal(DependencyState.Installed, Assert.Single(s.Status(G, "p", new[] { d }, None)).State);
        await s.SetKeptAsync(G, "p", false);
        Assert.False(s.IsKept(G, "p"));
    }

    [Fact]
    public async Task SetKept_and_RequestReinstall_without_a_ledger_create_nothing()
    {
        var s = Make();
        await s.SetKeptAsync(G, "p", true);
        await s.RequestReinstallAsync(G, "p");
        Assert.False(_fs.File.Exists(LedgerPath));
        Assert.False(s.IsKept(G, "p"));
    }

    [Fact]
    public async Task Flag_calls_with_an_invalid_plugin_id_do_nothing()
    {
        var s = Make();
        await s.SetKeptAsync(G, "../x", true);
        await s.RequestReinstallAsync(G, "../x");
        Assert.False(s.IsKept(G, "../x"));
        Assert.False(_fs.Directory.Exists("/game_mini/stellar/deps"));
    }

    // Round 6 rule: a present-but-unreadable ledger is never written blind.
    [Fact]
    public async Task SetKept_on_a_transiently_unreadable_ledger_throws_and_writes_nothing()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");
        await Make().EnsureAsync(G, "p", new[] { d }, None, default);
        var before = _fs.File.ReadAllText(LedgerPath);
        var faulty = new FaultInjectingFileSystem(_fs, LedgerPath, once: false, "ReadAllText");
        var s2 = new DependencyService(faulty, new HttpClient(new Stub(this)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => s2.SetKeptAsync(G, "p", true));

        Assert.Equal("dependency record could not be read", ex.Message);
        Assert.Equal(before, _fs.File.ReadAllText(LedgerPath));
    }

    // V3: "still parked for Vanilla launches" — and restored for Modded ones, staying kept.
    [Fact]
    public async Task A_kept_ledger_is_parked_for_vanilla_and_restored_for_modded()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll", modded: true);
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { d }, None, default);
        await s.SetKeptAsync(G, "p", true);

        await s.ParkModdedOnlyAsync(G);
        Assert.False(_fs.File.Exists("/game_mini/dxgi.dll"));
        Assert.True(_fs.File.Exists("/game_mini/stellar/deps-parked/p/dxgi.dll"));

        await s.UnparkModdedOnlyAsync(G);
        Assert.True(_fs.File.Exists("/game_mini/dxgi.dll"));
        Assert.True(s.IsKept(G, "p"));
    }

    // V3: "adopted again when the plugin is reinstalled" — the ensure after the reinstall clears the flag, nothing re-downloads.
    [Fact]
    public async Task Ensure_adopts_a_kept_ledger_without_downloading_again()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { d }, None, default);
        await s.SetKeptAsync(G, "p", true);
        _downloads = 0;

        Assert.Equal(DependencyState.Installed, Assert.Single(await s.EnsureAsync(G, "p", new[] { d }, None, default)).State);

        Assert.False(s.IsKept(G, "p"));
        Assert.Equal(0, _downloads);
    }

    // Controller brief: once adopted, the existing dropped-dependency cleanup (final review I3) applies.
    [Fact]
    public async Task Adopting_a_kept_ledger_removes_what_the_new_version_no_longer_declares()
    {
        var a = File("a", new byte[] { 1 }, "a.dll");
        var b = File("b", new byte[] { 2 }, "b.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { a, b }, None, default);
        await s.SetKeptAsync(G, "p", true);

        var st = await s.EnsureAsync(G, "p", new[] { a }, None, default);   // the reinstalled version dropped "b"

        Assert.Equal(DependencyState.Installed, Assert.Single(st).State);
        Assert.True(_fs.File.Exists("/game_mini/a.dll"));
        Assert.False(_fs.File.Exists("/game_mini/b.dll"));
        Assert.Equal(new[] { "a" }, LedgerOf().Entries.Select(e => e.DependencyId));
        Assert.False(LedgerOf().Kept);
    }

    [Fact]
    public async Task A_requested_reinstall_downloads_and_verifies_every_used_dependency_once_then_clears()
    {
        var a = File("a", new byte[] { 1 }, "a.dll");
        var b = File("b", new byte[] { 2 }, "b.dll", optional: true);
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { a, b }, None, default);
        await s.RequestReinstallAsync(G, "p");
        Assert.True(LedgerOf().ReinstallRequested);
        _downloads = 0;

        var st = await s.EnsureAsync(G, "p", new[] { a, b }, None, default);

        Assert.All(st, x => Assert.Equal(DependencyState.Installed, x.State));
        Assert.Equal(2, _downloads);
        Assert.False(LedgerOf().ReinstallRequested);
        _downloads = 0;
        await s.EnsureAsync(G, "p", new[] { a, b }, None, default);
        Assert.Equal(0, _downloads);   // consumed once
    }

    [Fact]
    public async Task A_requested_reinstall_never_downloads_a_skipped_dependency()
    {
        var a = File("a", new byte[] { 1 }, "a.dll");
        var b = File("b", new byte[] { 2 }, "b.dll", optional: true);
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { a, b }, None, default);
        await s.RequestReinstallAsync(G, "p");
        _downloads = 0;

        var st = await s.EnsureAsync(G, "p", new[] { a, b }, new HashSet<string> { "b" }, default);

        Assert.Equal(1, _downloads);
        Assert.Equal(DependencyState.Skipped, st[1].State);
        Assert.False(_fs.File.Exists("/game_mini/b.dll"));
    }

    // V2: "never overwriting a player's own file — the Blocked rule stands".
    [Fact]
    public async Task A_requested_reinstall_leaves_a_file_the_player_changed_and_reports_it_blocked()
    {
        var a = File("a", new byte[] { 1 }, "a.dll");
        var b = File("b", new byte[] { 2 }, "b.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { a, b }, None, default);
        _fs.File.WriteAllBytes("/game_mini/b.dll", new byte[] { 7 });   // the player's edit
        await s.RequestReinstallAsync(G, "p");

        var st = await s.EnsureAsync(G, "p", new[] { a, b }, None, default);

        Assert.Equal(DependencyState.Installed, st[0].State);
        Assert.Equal(DependencyState.Blocked, st[1].State);
        Assert.Equal(DependencyReason.PlayerFile, st[1].Reason);
        Assert.Equal(new byte[] { 7 }, _fs.File.ReadAllBytes("/game_mini/b.dll"));
    }

    // Review carry-over (c): the request itself throws like SetKeptAsync on a present-but-unreadable ledger — a
    // flag write is never blind — and touches nothing.
    [Fact]
    public async Task RequestReinstall_on_a_transiently_unreadable_ledger_throws_and_writes_nothing()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");
        await Make().EnsureAsync(G, "p", new[] { d }, None, default);
        var before = _fs.File.ReadAllText(LedgerPath);
        var faulty = new FaultInjectingFileSystem(_fs, LedgerPath, once: false, "ReadAllText");
        var s2 = new DependencyService(faulty, new HttpClient(new Stub(this)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => s2.RequestReinstallAsync(G, "p"));

        Assert.Equal("dependency record could not be read", ex.Message);
        Assert.Equal(before, _fs.File.ReadAllText(LedgerPath));
    }

    // Review carry-over (c): a ledger that is BOTH kept and reinstall-requested adopts (clears Kept) AND
    // reinstalls (re-downloads, clears ReinstallRequested) in the same ensure.
    [Fact]
    public async Task A_ledger_that_is_both_kept_and_reinstall_requested_adopts_and_reinstalls_in_one_ensure()
    {
        var a = File("a", new byte[] { 1 }, "a.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { a }, None, default);
        await s.SetKeptAsync(G, "p", true);
        await s.RequestReinstallAsync(G, "p");
        Assert.True(LedgerOf().Kept);
        Assert.True(LedgerOf().ReinstallRequested);
        _downloads = 0;

        var st = await s.EnsureAsync(G, "p", new[] { a }, None, default);

        Assert.Equal(DependencyState.Installed, Assert.Single(st).State);
        Assert.False(s.IsKept(G, "p"));
        Assert.False(LedgerOf().ReinstallRequested);
        Assert.Equal(1, _downloads);   // re-downloaded even though adopted, not merely left alone
    }

    // Review carry-over (b): a remove that fails during a requested reinstall must NOT clear the flag — otherwise
    // the still-wrong dependency is never retried.
    [Fact]
    public async Task A_failed_remove_during_a_requested_reinstall_leaves_the_flag_set_for_a_retry()
    {
        var a = File("a", new byte[] { 1 }, "a.dll");
        var b = File("b", new byte[] { 2 }, "b.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { a, b }, None, default);
        await s.RequestReinstallAsync(G, "p");

        // Let RemoveUndeclared's own read (call 1) and "a"'s RemoveCore read (call 2) through; fail "b"'s.
        var faulty = new FaultInjectingFileSystem(_fs, LedgerPath, once: true, afterCalls: 2, "ReadAllText");
        var s2 = new DependencyService(faulty, new HttpClient(new Stub(this)));

        await s2.EnsureAsync(G, "p", new[] { a, b }, None, default);

        Assert.True(LedgerOf().ReinstallRequested);   // "b" never got re-downloaded — the next ensure must retry it
    }

    // Review carry-over (a): the gated check used by the orphan sweep — a present-but-unreadable ledger is
    // treated as KEPT (never removed), not silently swept on a transient read failure.
    [Fact]
    public async Task RemoveAllUnlessKept_on_a_transiently_unreadable_ledger_removes_nothing()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");
        await Make().EnsureAsync(G, "p", new[] { d }, None, default);
        var before = _fs.File.ReadAllText(LedgerPath);
        var faulty = new FaultInjectingFileSystem(_fs, LedgerPath, once: false, "ReadAllText");
        var s2 = new DependencyService(faulty, new HttpClient(new Stub(this)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => s2.RemoveAllUnlessKeptAsync(G, "p"));

        Assert.Equal("dependency record could not be read", ex.Message);
        Assert.Equal(before, _fs.File.ReadAllText(LedgerPath));
        Assert.True(_fs.File.Exists("/game_mini/dxgi.dll"));
    }

    [Fact]
    public async Task RemoveAllUnlessKept_removes_an_ordinary_ledger_and_returns_true()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { d }, None, default);

        Assert.True(await s.RemoveAllUnlessKeptAsync(G, "p"));

        Assert.False(_fs.File.Exists("/game_mini/dxgi.dll"));
        Assert.False(_fs.File.Exists(LedgerPath));
    }

    [Fact]
    public async Task RemoveAllUnlessKept_leaves_a_kept_ledger_untouched_and_returns_false()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { d }, None, default);
        await s.SetKeptAsync(G, "p", true);

        Assert.False(await s.RemoveAllUnlessKeptAsync(G, "p"));

        Assert.True(_fs.File.Exists("/game_mini/dxgi.dll"));
        Assert.True(LedgerOf().Kept);
    }
}

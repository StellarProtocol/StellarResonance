using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>Final review I3: a dependency a plugin update dropped or renamed — or all of them, when the new
/// version declares none — is cleaned up, with the same hash-checked removal as an unticked one. Shares
/// helpers/fields with <see cref="DependencyServiceTests"/>.</summary>
public sealed partial class DependencyServiceTests
{
    [Fact]
    public async Task A_dependency_dropped_by_an_update_is_removed()
    {
        var s = Make();
        var keep = File("keep", new byte[] { 1 }, "keep.dll");
        await s.EnsureAsync(G, "p", new[] { keep, File("gone", new byte[] { 2 }, "gone.dll", modded: true) }, None, default);

        var st = await s.EnsureAsync(G, "p", new[] { keep }, None, default);

        Assert.Equal(DependencyState.Installed, Assert.Single(st).State);
        Assert.False(_fs.File.Exists("/game_mini/gone.dll"));
        Assert.True(_fs.File.Exists("/game_mini/keep.dll"));
        Assert.Equal(new[] { "keep" }, new DependencyLedgerStore(_fs).Read(G, "p").Entries.Select(e => e.DependencyId));
    }

    [Fact]
    public async Task A_renamed_dependency_takes_over_its_old_destination_instead_of_blocking_on_it()
    {
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { File("old", new byte[] { 1 }, "dxgi.dll") }, None, default);

        var st = Assert.Single(await s.EnsureAsync(G, "p", new[] { File("new", new byte[] { 2 }, "dxgi.dll") }, None, default));

        Assert.Equal(DependencyState.Installed, st.State);
        Assert.Equal(new byte[] { 2 }, _fs.File.ReadAllBytes("/game_mini/dxgi.dll"));
        Assert.Equal(new[] { "new" }, new DependencyLedgerStore(_fs).Read(G, "p").Entries.Select(e => e.DependencyId));
    }

    [Fact]
    public async Task An_empty_declaration_removes_everything_this_plugin_placed_and_its_parked_copies()
    {
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { File("fx", new byte[] { 1 }, "dxgi.dll", modded: true) }, None, default);
        await s.ParkModdedOnlyAsync(G);
        Assert.True(_fs.File.Exists("/game_mini/stellar/deps-parked/p/dxgi.dll"));

        Assert.Empty(await s.EnsureAsync(G, "p", Array.Empty<PluginDependency>(), None, default));

        Assert.False(_fs.File.Exists("/game_mini/stellar/deps-parked/p/dxgi.dll"));
        Assert.False(_fs.File.Exists("/game_mini/stellar/deps/p.json"));
    }

    [Fact]
    public async Task A_dropped_dependencys_file_the_player_changed_is_left_alone()
    {
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { File("gone", new byte[] { 2 }, "gone.dll") }, None, default);
        _fs.File.WriteAllBytes("/game_mini/gone.dll", new byte[] { 7 });

        await s.EnsureAsync(G, "p", Array.Empty<PluginDependency>(), None, default);

        Assert.Equal(new byte[] { 7 }, _fs.File.ReadAllBytes("/game_mini/gone.dll"));
    }

    [Fact]
    public async Task An_unreadable_ledger_never_lets_the_sweep_delete_anything_and_never_fails_the_declared_ones()
    {
        await Make().EnsureAsync(G, "p", new[] { File("gone", new byte[] { 2 }, "gone.dll") }, None, default);
        var json = _fs.File.ReadAllText(LedgerPath);
        // Every ledger read fails: the sweep must skip, and nothing is deleted.
        var s2 = new DependencyService(new FaultInjectingFileSystem(_fs, LedgerPath, once: false, "ReadAllText"), new HttpClient(new Stub(this)));

        var st = await s2.EnsureAsync(G, "p", Array.Empty<PluginDependency>(), None, default);

        Assert.Empty(st);
        Assert.True(_fs.File.Exists("/game_mini/gone.dll"));
        Assert.Equal(json, _fs.File.ReadAllText(LedgerPath));
    }
}

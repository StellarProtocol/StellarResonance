using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using StellarLauncher.Core.Dependencies;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>Final review M-e: a parked file whose destination the player has since filled with their own file
/// turns Blocked "(modified)" and is dropped from the ledger — so its parked copy, which nothing would ever
/// restore or remove again, is deleted (hash-checked). Shares helpers/fields with <see cref="DependencyServiceTests"/>.</summary>
public sealed partial class DependencyServiceTests
{
    private const string ParkedDxgi = "/game_mini/stellar/deps-parked/p/dxgi.dll";

    [Fact]
    public async Task A_parked_copy_is_deleted_when_its_destination_turns_Blocked_modified()
    {
        var d = File("fx", new byte[] { 1, 2, 3 }, "dxgi.dll", modded: true);
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { d }, None, default);
        await s.ParkModdedOnlyAsync(G);                                            // a vanilla launch
        _fs.AddFile("/game_mini/dxgi.dll", new MockFileData(new byte[] { 9 }));    // the player's own file meanwhile
        await s.UnparkModdedOnlyAsync(G);                                          // occupied: stays parked

        var st = Assert.Single(await s.EnsureAsync(G, "p", new[] { d }, None, default));

        Assert.Equal(DependencyState.Blocked, st.State);
        Assert.Equal("dxgi.dll (modified)", st.Detail);
        Assert.False(_fs.File.Exists(ParkedDxgi));
        Assert.Equal(new byte[] { 9 }, _fs.File.ReadAllBytes("/game_mini/dxgi.dll"));
    }

    [Fact]
    public async Task A_parked_copy_that_is_not_our_recorded_bytes_is_kept()
    {
        var d = File("fx", new byte[] { 1, 2, 3 }, "dxgi.dll", modded: true);
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { d }, None, default);
        await s.ParkModdedOnlyAsync(G);
        _fs.File.WriteAllBytes(ParkedDxgi, new byte[] { 5 });                      // something else rewrote it
        _fs.AddFile("/game_mini/dxgi.dll", new MockFileData(new byte[] { 9 }));

        Assert.Equal(DependencyState.Blocked, Assert.Single(await s.EnsureAsync(G, "p", new[] { d }, None, default)).State);
        Assert.Equal(new byte[] { 5 }, _fs.File.ReadAllBytes(ParkedDxgi));
    }
}

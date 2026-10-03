using System.Threading.Tasks;
using StellarLauncher.Core.Dependencies;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>Controller round: every public entry point validates <c>pluginId</c> with the same rule
/// <see cref="DependencyLedgerStore"/> uses for ledger-file stems (<see cref="DependencyPaths.IsValidPluginId"/>)
/// before it is ever turned into a path segment. Shares helpers/fields with <see cref="DependencyServiceTests"/>.</summary>
public sealed partial class DependencyServiceTests
{
    private const string BadId = "../evil";

    [Fact]
    public async Task EnsureAsync_with_an_invalid_plugin_id_fails_every_dependency_without_any_io()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");
        var st = Assert.Single(await Make().EnsureAsync(G, BadId, new[] { d }, None, default));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.Equal("invalid plugin id", st.Detail);
        Assert.Equal(0, _downloads);
        Assert.False(_fs.File.Exists("/game_mini/dxgi.dll"));
    }

    [Fact]
    public void Status_with_an_invalid_plugin_id_is_empty()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");
        Assert.Empty(Make().Status(G, BadId, new[] { d }, None));
    }

    [Fact]
    public async Task Remove_with_an_invalid_plugin_id_is_a_noop()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { d }, None, default);

        s.Remove(G, BadId, "fx"); // must not throw, must not touch plugin "p"'s own ledger/files

        Assert.True(_fs.File.Exists("/game_mini/dxgi.dll"));
        Assert.Single(new DependencyLedgerStore(_fs).Read(G, "p").Entries);
    }

    [Fact]
    public async Task RemoveAll_with_an_invalid_plugin_id_is_a_noop()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { d }, None, default);

        s.RemoveAll(G, BadId); // must not throw, must not touch plugin "p"'s own ledger/files

        Assert.True(_fs.File.Exists("/game_mini/dxgi.dll"));
        Assert.Single(new DependencyLedgerStore(_fs).Read(G, "p").Entries);
    }
}

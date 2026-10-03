using StellarLauncher.Core.Dependencies;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

public sealed class DependencyPathsTests
{
    private const string G = "/game_mini";

    [Fact] public void Game_target_resolves_under_game_mini() =>
        Assert.Equal("/game_mini/dxgi.dll", DependencyPaths.Resolve(G, "p", "game", "dxgi.dll"));

    [Fact] public void Plugin_target_resolves_under_stellar_deps() =>
        Assert.Equal("/game_mini/stellar/deps/p/fx/a.fx", DependencyPaths.Resolve(G, "p", "plugin", "fx/a.fx"));

    [Theory]
    [InlineData("game", "../x.dll")] [InlineData("game", "/abs")] [InlineData("game", "a\\b")]
    [InlineData("game", "BepInEx/plugins/x.dll")] [InlineData("game", "stellar/plugins/x.dll")]
    [InlineData("game", "Stellar/Deps/x")] [InlineData("plugin", "../../x")] [InlineData("other", "x")]
    [InlineData("game", "C:/x")] [InlineData("plugin", "c:/y")] [InlineData("game", "ab:cd")]
    public void Disallowed_paths_resolve_to_null(string target, string to) =>
        Assert.Null(DependencyPaths.Resolve(G, "p", target, to));

    [Fact] public void Relative_uses_forward_slashes() =>
        Assert.Equal("stellar/deps/p/a.fx", DependencyPaths.Relative(G, "/game_mini/stellar/deps/p/a.fx"));
}

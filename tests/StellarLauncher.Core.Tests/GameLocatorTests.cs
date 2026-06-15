using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.Core.Services;
using Xunit;

public class GameLocatorTests
{
    [Fact]
    public void Picks_newest_release_dir_with_game_mini()
    {
        var fs = new MockFileSystem();
        var root = "/game";
        fs.AddDirectory($"{root}/release_2.9/game_mini");
        fs.AddDirectory($"{root}/release_2.11/game_mini");
        fs.AddDirectory($"{root}/release_2.10");           // no game_mini → ignored
        var locator = new GameLocator(fs);

        var found = locator.FindGameMini(root);

        Assert.Equal(fs.Path.Combine(root, "release_2.11", "game_mini"),
                     found);
    }

    [Fact]
    public void Returns_null_when_no_valid_release()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/game/release_2.11");             // no game_mini
        var locator = new GameLocator(fs);
        Assert.Null(locator.FindGameMini("/game"));
    }
}

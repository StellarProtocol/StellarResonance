using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.Core.Services;
using Xunit;

public class GameDetectorTests
{
    [Fact]
    public void Detects_game_mini_under_a_wine_prefix_search_root()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/opt/game/BP2/drive_c/Star/StarLauncher/game/release_2.11/game_mini");
        var detector = new GameDetector(fs, new GameLocator(fs),
            () => new[] { "/opt/game/BP2", "/opt/game/Empty" });

        var found = detector.Detect();

        var expected = fs.Path.Combine("/opt/game/BP2", "drive_c", "Star",
            "StarLauncher", "game", "release_2.11", "game_mini");
        Assert.Contains(expected, found);
    }

    [Fact]
    public void Picks_newest_release_per_root_and_dedupes()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/p/drive_c/Star/StarLauncher/game/release_2.9/game_mini");
        fs.AddDirectory("/p/drive_c/Star/StarLauncher/game/release_2.11/game_mini");
        var detector = new GameDetector(fs, new GameLocator(fs), () => new[] { "/p", "/p" });

        var found = detector.Detect();

        Assert.Single(found);  // newest only, deduped despite the repeated root
        Assert.EndsWith(fs.Path.Combine("release_2.11", "game_mini"), found[0]);
    }

    [Fact]
    public void Returns_empty_when_nothing_matches()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/opt/game/Empty/whatever");
        var detector = new GameDetector(fs, new GameLocator(fs), () => new[] { "/opt/game/Empty" });
        Assert.Empty(detector.Detect());
    }

    [Theory]
    [InlineData("/opt/game/BP2/drive_c/Star/StarLauncher/game/release_2.11/game_mini", "/opt/game/BP2")]
    [InlineData("C:\\Star\\StarLauncher\\game\\release_2.11\\game_mini", null)]   // native Windows → no prefix
    public void WinePrefixFor_strips_at_drive_c(string gameMini, string? expected)
        => Assert.Equal(expected, GameDetector.WinePrefixFor(gameMini));

    [Fact]
    public void DetectRunner_picks_first_existing_candidate()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/home/u/.config/heroic/tools/proton/GE-Proton10-26/proton", new MockFileData("x"));
        var detector = new GameDetector(fs, new GameLocator(fs),
            () => System.Array.Empty<string>(),
            () => new[]
            {
                "/home/u/.config/heroic/tools/proton/GE-Proton99/proton",   // missing → skipped
                "/home/u/.config/heroic/tools/proton/GE-Proton10-26/proton", // exists → picked
                "/usr/bin/wine",
            });

        Assert.Equal("/home/u/.config/heroic/tools/proton/GE-Proton10-26/proton", detector.DetectRunner());
    }
}

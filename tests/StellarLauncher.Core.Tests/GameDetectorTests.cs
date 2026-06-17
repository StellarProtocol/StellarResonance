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
    public void Detects_via_StarLauncher_parent_root_without_a_Star_folder()
    {
        // Registry-derived root: the parent of the StarLauncher dir, with no "Star" folder above it.
        var fs = new MockFileSystem();
        fs.AddDirectory("/custom/StarLauncher/game/release_2.11/game_mini");
        var detector = new GameDetector(fs, new GameLocator(fs), () => new[] { "/custom" });

        var found = detector.Detect();

        Assert.Contains(
            fs.Path.Combine("/custom", "StarLauncher", "game", "release_2.11", "game_mini"),
            found);
    }

    [Fact]
    public void Detects_JP_StarASIA_install_under_an_arbitrary_drive_subfolder()
    {
        // JP StarASIA client: <drive>\<arbitrary>\StarLauncher\game\release_*\game_mini (e.g.
        // E:\bpsr\StarLauncher\…) — no "Star" parent. BuildSearchRoots surfaces each drive's children, so the
        // arbitrary parent folder ("bpsr") becomes a search root and LauncherSuffix resolves the install.
        var fs = new MockFileSystem();
        fs.AddDirectory("/e/bpsr/StarLauncher/game/release_2.11/game_mini");
        var detector = new GameDetector(fs, new GameLocator(fs), () => new[] { "/e/bpsr" });

        var found = detector.Detect();

        Assert.Contains(
            fs.Path.Combine("/e/bpsr", "StarLauncher", "game", "release_2.11", "game_mini"),
            found);
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

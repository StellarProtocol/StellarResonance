using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.Core.Inventory;
using Xunit;

public class GameBuildTests
{
    private const string G = "/opt/game/P/drive_c/Star/StarLauncher/game/release_3.7/game_mini";

    private static (string region, string edition) Detect(string dataFolder)
    {
        var fs = new MockFileSystem();
        fs.AddDirectory($"{G}/{dataFolder}");
        return GameBuild.Detect(fs, G);
    }

    [Fact]
    public void Sea_standalone_from_StarSEA_Data() => Assert.Equal(("SEA", "Standalone"), Detect("StarSEA_Data"));

    // A JP (StarASIA) install can sit under the SAME Star/StarLauncher path as SEA — the data folder is the truth.
    [Fact]
    public void Jp_standalone_from_StarASIA_Data() => Assert.Equal(("JP", "Standalone"), Detect("StarASIA_Data"));

    [Fact]
    public void Steam_from_StarSEA_STEAM_Data() => Assert.Equal(("SEA", "Steam"), Detect("StarSEA_STEAM_Data"));

    [Fact]
    public void Empty_when_no_data_folder()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(G);
        Assert.Equal(("", ""), GameBuild.Detect(fs, G));
    }

    [Fact]
    public void Empty_when_folder_missing() => Assert.Equal(("", ""), GameBuild.Detect(new MockFileSystem(), G));
}

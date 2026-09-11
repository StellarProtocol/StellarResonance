using StellarLauncher.Core.Clients;
using Xunit;

public class ClientNamingTests
{
    [Theory]
    [InlineData("/opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_3.7/game_mini", "BlueProtocol2")]
    [InlineData("/home/u/Games/Heroic/Prefixes/StarASIA/drive_c/StarLauncher/game/release_3.7/game_mini", "StarASIA")]
    [InlineData(@"E:\bpsr\StarLauncher\game\release_3.7\game_mini", "bpsr")]
    [InlineData(@"E:\Star\StarLauncher\game\release_3.7\game_mini", "Main")]          // only generic segments + drive
    [InlineData(@"D:\SteamLibrary\steamapps\common\Blue Protocol Star Resonance", "Blue Protocol Star Resonance")]
    [InlineData("/home/u/.steam/steam/steamapps/compatdata/123456/pfx/drive_c/Star/StarLauncher/game/release_3.7/game_mini", "123456")]
    public void ProposeName_walks_up_past_generic_segments(string path, string expected)
        => Assert.Equal(expected, ClientNaming.ProposeName(path));

    [Theory]
    [InlineData("/opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_3.7/game_mini", ClientLayout.SeaStarLauncher)]
    [InlineData("/home/u/Games/Heroic/Prefixes/StarASIA/drive_c/StarLauncher/game/release_3.7/game_mini", ClientLayout.JpStarLauncher)]
    [InlineData(@"E:\bpsr\StarLauncher\game\release_3.7\game_mini", ClientLayout.JpStarLauncher)]
    [InlineData(@"D:\SteamLibrary\steamapps\common\Blue Protocol Star Resonance", ClientLayout.SteamFlat)]
    [InlineData("/somewhere/else", ClientLayout.Unknown)]
    public void DetectLayout(string path, ClientLayout expected)
        => Assert.Equal(expected, ClientNaming.DetectLayout(path));

    [Fact]
    public void Wine_prefix_detection_and_tags()
    {
        Assert.True(ClientNaming.IsWinePrefixPath("/opt/game/BlueProtocol/drive_c/Star/StarLauncher/game/release_3.7/game_mini"));
        Assert.False(ClientNaming.IsWinePrefixPath(@"E:\Star\StarLauncher\game\release_3.7\game_mini"));
        Assert.Equal("SEA · StarLauncher", ClientNaming.LayoutTag(ClientLayout.SeaStarLauncher));
        Assert.Equal("JP layout", ClientNaming.LayoutTag(ClientLayout.JpStarLauncher));
        Assert.Equal("Steam", ClientNaming.LayoutTag(ClientLayout.SteamFlat));
        Assert.Equal("", ClientNaming.LayoutTag(ClientLayout.Unknown));
    }
}

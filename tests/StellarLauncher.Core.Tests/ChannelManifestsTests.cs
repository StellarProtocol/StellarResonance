using StellarLauncher.Core.Services;
using Xunit;

public class ChannelManifestsTests
{
    [Theory]
    [InlineData("stable",  "https://cdn.revette.io/version.json",         "https://cdn.revette.io/launcher.json")]
    [InlineData(null,      "https://cdn.revette.io/version.json",         "https://cdn.revette.io/launcher.json")]
    [InlineData("testing", "https://cdn.revette.io/version-testing.json", "https://cdn.revette.io/launcher-testing.json")]
    [InlineData("TESTING", "https://cdn.revette.io/version-testing.json", "https://cdn.revette.io/launcher-testing.json")]
    public void Maps_channel_to_urls(string? channel, string frameworkUrl, string launcherUrl)
    {
        Assert.Equal(frameworkUrl, ChannelManifests.FrameworkVersion(channel).ToString());
        Assert.Equal(launcherUrl, ChannelManifests.LauncherManifest(channel).ToString());
    }
}

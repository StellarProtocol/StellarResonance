using StellarLauncher.Core.Services;
using Xunit;

public class ChannelManifestsTests
{
    [Theory]
    [InlineData("stable",  "https://minio.revette.io/stellar/version.json",         "https://minio.revette.io/stellar/launcher.json")]
    [InlineData(null,      "https://minio.revette.io/stellar/version.json",         "https://minio.revette.io/stellar/launcher.json")]
    [InlineData("testing", "https://minio.revette.io/stellar/version-testing.json", "https://minio.revette.io/stellar/launcher-testing.json")]
    [InlineData("TESTING", "https://minio.revette.io/stellar/version-testing.json", "https://minio.revette.io/stellar/launcher-testing.json")]
    public void Maps_channel_to_urls(string? channel, string frameworkUrl, string launcherUrl)
    {
        Assert.Equal(frameworkUrl, ChannelManifests.FrameworkVersion(channel).ToString());
        Assert.Equal(launcherUrl, ChannelManifests.LauncherManifest(channel).ToString());
    }
}

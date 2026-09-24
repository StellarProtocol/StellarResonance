using System;
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

    [Fact]
    public void STELLAR_CDN_BASE_overrides_the_host_for_testing_or_staging()
    {
        Environment.SetEnvironmentVariable("STELLAR_CDN_BASE", "http://localhost:9977/");   // trailing slash tolerated
        try
        {
            Assert.Equal("http://localhost:9977/version.json", ChannelManifests.FrameworkVersion("stable").ToString());
            Assert.Equal("http://localhost:9977/plugins.json", ChannelManifests.PluginRegistry("stable").ToString());
            Assert.Equal("http://localhost:9977/launcher.json", ChannelManifests.LauncherManifest("stable").ToString());
        }
        finally { Environment.SetEnvironmentVariable("STELLAR_CDN_BASE", null); }
    }
}

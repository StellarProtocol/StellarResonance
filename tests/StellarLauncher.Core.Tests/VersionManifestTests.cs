using System.Text.Json;
using StellarLauncher.Core.Model;
using Xunit;

public class VersionManifestTests
{
    private const string Json = """
    {
      "version": "1.0.0",
      "date": "2026-06-08",
      "bundleUrl": "https://example.com/Stellar-1.0.0.zip",
      "sha256": "abc123",
      "minLauncherVersion": "1.0.0",
      "changelog": { "added": ["A thing"], "changed": [], "fixed": ["A bug"], "removed": [] }
    }
    """;

    [Fact]
    public void Deserializes_all_fields()
    {
        var m = JsonSerializer.Deserialize<VersionManifest>(Json, VersionManifest.JsonOptions)!;
        Assert.Equal("1.0.0", m.Version);
        Assert.Equal("2026-06-08", m.Date);
        Assert.Equal("https://example.com/Stellar-1.0.0.zip", m.BundleUrl);
        Assert.Equal("abc123", m.Sha256);
        Assert.Equal("1.0.0", m.MinLauncherVersion);
        Assert.Equal(new[] { "A thing" }, m.Changelog.Added);
        Assert.Equal(new[] { "A bug" }, m.Changelog.Fixed);
        Assert.Empty(m.Changelog.Changed);
    }

    [Fact]
    public void FrameworkManifest_parses_version_history()
    {
        const string history = """
        {
          "latest": "1.4.0",
          "channel": "stable",
          "versions": [
            { "version":"1.4.0","date":"2026-06-10","bundleUrl":"u4","sha256":"s4",
              "minLauncherVersion":"1.0.0",
              "changelog":{"added":["x"],"changed":[],"fixed":[],"removed":[]} },
            { "version":"1.3.0","date":"2026-06-01","bundleUrl":"u3","sha256":"s3",
              "minLauncherVersion":"1.0.0",
              "changelog":{"added":[],"changed":[],"fixed":[],"removed":[]} }
          ]
        }
        """;
        var m = JsonSerializer.Deserialize<FrameworkManifest>(history, FrameworkManifest.JsonOptions)!;
        Assert.Equal("1.4.0", m.Latest);
        Assert.Equal("stable", m.Channel);
        Assert.Equal(2, m.Versions.Count);
        Assert.Equal("1.4.0", m.Versions[0].Version);
        Assert.Equal(new[] { "x" }, m.Versions[0].Changelog.Added);
    }
}

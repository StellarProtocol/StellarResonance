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
}

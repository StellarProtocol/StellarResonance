using System.Text.Json;
using StellarLauncher.Core.Model;
using Xunit;
namespace StellarLauncher.Core.Tests;

public sealed class PluginDependencyModelTests
{
    private const string Json = """
    { "plugins": [ { "id": "p", "name": "P", "description": "d", "author": "a", "versions": [ {
        "version": "1.0.0", "dllUrl": "https://x/p.dll", "sha256": "00", "minModSystemVersion": "2.0.0",
        "dependencies": [ { "id": "fx", "name": "FX", "version": "6.8.0", "url": "https://cdn/fx.dll",
          "sha256": "ab", "size": 42, "kind": "file", "files": [ { "to": "dxgi.dll" } ], "target": "game",
          "moddedOnly": true, "optional": true, "requires": [], "license": "BSD-3-Clause",
          "licenseUrl": "https://l", "sourceUrl": "https://s", "notice": "n" } ] } ] } ] }
    """;

    [Fact]
    public void Dependencies_deserialize_with_every_field()
    {
        var reg = JsonSerializer.Deserialize<PluginRegistry>(Json, PluginRegistry.JsonOptions)!;
        var d = Assert.Single(reg.Plugins[0].Versions[0].Dependencies!);
        // Core identity fields
        Assert.Equal("fx", d.Id);
        Assert.Equal("FX", d.Name);
        Assert.Equal("6.8.0", d.Version);
        Assert.Equal("https://cdn/fx.dll", d.Url);
        Assert.Equal("ab", d.Sha256);
        Assert.Equal(42L, d.Size);
        // Kind and placement
        Assert.Equal("file", d.Kind);
        Assert.Equal("game", d.Target);
        Assert.Equal("dxgi.dll", Assert.Single(d.Files).To);
        // Flags and optional fields
        Assert.True(d.ModdedOnly && d.Optional);
        Assert.Empty(d.Requires!);
        Assert.Equal("BSD-3-Clause", d.License);
        Assert.Equal("https://l", d.LicenseUrl);
        Assert.Equal("https://s", d.SourceUrl);
        Assert.Equal("n", d.Notice);
    }

    [Fact]
    public void A_version_without_dependencies_reads_null() =>
        Assert.Null(JsonSerializer.Deserialize<PluginRegistry>(Json.Replace("\"dependencies\"", "\"other\""),
            PluginRegistry.JsonOptions)!.Plugins[0].Versions[0].Dependencies);

    // v3 V1: the install step shows "what it does" — an optional manifest string, absent in older manifests.
    [Fact]
    public void Description_is_optional_and_read_when_present()
    {
        var reg = JsonSerializer.Deserialize<PluginRegistry>(Json, PluginRegistry.JsonOptions)!;
        Assert.Null(Assert.Single(reg.Plugins[0].Versions[0].Dependencies!).Description);

        var withText = Json.Replace("\"notice\": \"n\"", "\"notice\": \"n\", \"description\": \"Adds effects.\"");
        var reg2 = JsonSerializer.Deserialize<PluginRegistry>(withText, PluginRegistry.JsonOptions)!;
        Assert.Equal("Adds effects.", Assert.Single(reg2.Plugins[0].Versions[0].Dependencies!).Description);
    }
}

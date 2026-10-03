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
        Assert.Equal(("fx", "6.8.0", "file", "game", 42L), (d.Id, d.Version, d.Kind, d.Target, d.Size));
        Assert.True(d.ModdedOnly && d.Optional);
        Assert.Equal("dxgi.dll", Assert.Single(d.Files).To);
        Assert.Equal(("BSD-3-Clause", "n"), (d.License, d.Notice));
    }

    [Fact]
    public void A_version_without_dependencies_reads_null() =>
        Assert.Null(JsonSerializer.Deserialize<PluginRegistry>(Json.Replace("\"dependencies\"", "\"other\""),
            PluginRegistry.JsonOptions)!.Plugins[0].Versions[0].Dependencies);
}

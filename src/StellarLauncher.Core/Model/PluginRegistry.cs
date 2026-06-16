using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StellarLauncher.Core.Model;

// One published build of a plugin. Declares the framework (modsystem) range it runs on so the
// launcher can gate it against the installed framework version. See docs/manifest-standard.md.
public sealed record PluginVersion(
    [property: JsonPropertyName("version")]             string Version,
    [property: JsonPropertyName("date")]                string? Date,
    [property: JsonPropertyName("dll")]                 string? Dll,      // canonical on-disk filename (assembly name)
    [property: JsonPropertyName("dllUrl")]              string DllUrl,
    [property: JsonPropertyName("sha256")]              string Sha256,
    [property: JsonPropertyName("minModSystemVersion")] string MinModSystemVersion,
    [property: JsonPropertyName("maxModSystemVersion")] string? MaxModSystemVersion,
    [property: JsonPropertyName("changelog")]           Changelog? Changelog);

public sealed record PluginEntry(
    [property: JsonPropertyName("id")]          string Id,
    [property: JsonPropertyName("name")]        string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("author")]      string? Author,
    [property: JsonPropertyName("versions")]    IReadOnlyList<PluginVersion> Versions);

public sealed record PluginRegistry(
    [property: JsonPropertyName("plugins")] IReadOnlyList<PluginEntry> Plugins)
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}

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
    [property: JsonPropertyName("changelog")]           Changelog? Changelog,
    [property: JsonPropertyName("sourceRepository")]    string? SourceRepository = null,   // display-only provenance
    [property: JsonPropertyName("sourceTag")]           string? SourceTag = null);

// One media item on a plugin's detail page: a screenshot or a YouTube video.
// "image" → Url is the picture itself; "youtube" → Url is a watch/short/embed link the
// launcher opens in the system browser (it only ever downloads the thumbnail).
public sealed record PluginMedia(
    [property: JsonPropertyName("type")]    string Type,
    [property: JsonPropertyName("url")]     string Url,
    [property: JsonPropertyName("caption")] string? Caption);

public sealed record PluginEntry(
    [property: JsonPropertyName("id")]          string Id,
    [property: JsonPropertyName("name")]        string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("author")]      string? Author,
    [property: JsonPropertyName("versions")]    IReadOnlyList<PluginVersion> Versions,
    [property: JsonPropertyName("tags")]        IReadOnlyList<string>? Tags = null,
    [property: JsonPropertyName("homepage")]    string? Homepage = null,
    [property: JsonPropertyName("media")]       IReadOnlyList<PluginMedia>? Media = null,
    [property: JsonPropertyName("guideUrl")]    string? GuideUrl = null);

public sealed record PluginRegistry(
    [property: JsonPropertyName("plugins")] IReadOnlyList<PluginEntry> Plugins)
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}

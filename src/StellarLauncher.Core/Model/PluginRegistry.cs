using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StellarLauncher.Core.Model;

public sealed record PluginEntry(
    [property: JsonPropertyName("id")]          string Id,
    [property: JsonPropertyName("name")]        string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("version")]     string Version,
    [property: JsonPropertyName("dllUrl")]      string DllUrl,
    [property: JsonPropertyName("sha256")]      string Sha256,
    [property: JsonPropertyName("author")]      string? Author);

public sealed record PluginRegistry(
    [property: JsonPropertyName("plugins")] IReadOnlyList<PluginEntry> Plugins)
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}

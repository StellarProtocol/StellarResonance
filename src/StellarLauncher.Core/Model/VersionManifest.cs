// src/StellarLauncher.Core/Model/VersionManifest.cs
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StellarLauncher.Core.Model;

public sealed record Changelog(
    [property: JsonPropertyName("added")]   IReadOnlyList<string> Added,
    [property: JsonPropertyName("changed")] IReadOnlyList<string> Changed,
    [property: JsonPropertyName("fixed")]   IReadOnlyList<string> Fixed,
    [property: JsonPropertyName("removed")] IReadOnlyList<string> Removed);

public sealed record VersionManifest(
    [property: JsonPropertyName("version")]            string Version,
    [property: JsonPropertyName("date")]               string Date,
    [property: JsonPropertyName("bundleUrl")]          string BundleUrl,
    [property: JsonPropertyName("sha256")]             string Sha256,
    [property: JsonPropertyName("minLauncherVersion")] string MinLauncherVersion,
    [property: JsonPropertyName("changelog")]          Changelog Changelog)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

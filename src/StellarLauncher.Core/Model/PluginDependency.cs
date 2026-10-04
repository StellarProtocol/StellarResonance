using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StellarLauncher.Core.Model;

// One file a dependency places. For kind "file", From is unused; for "zip", From is an entry path, or a prefix ending in '/'.
public sealed record PluginDependencyFile(
    [property: JsonPropertyName("from")] string? From,
    [property: JsonPropertyName("to")]   string To);

// Something a plugin needs installed alongside it (docs/manifest-standard.md § dependencies). The launcher is generic:
// it downloads, verifies and places these without knowing what they are.
public sealed record PluginDependency(
    [property: JsonPropertyName("id")]         string Id,
    [property: JsonPropertyName("name")]       string Name,
    [property: JsonPropertyName("version")]    string Version,
    [property: JsonPropertyName("url")]        string Url,
    [property: JsonPropertyName("sha256")]     string Sha256,
    [property: JsonPropertyName("size")]       long Size,
    [property: JsonPropertyName("kind")]       string Kind,
    [property: JsonPropertyName("files")]      IReadOnlyList<PluginDependencyFile> Files,
    [property: JsonPropertyName("target")]     string Target,
    [property: JsonPropertyName("moddedOnly")] bool ModdedOnly = false,
    [property: JsonPropertyName("optional")]   bool Optional = false,
    [property: JsonPropertyName("requires")]   IReadOnlyList<string>? Requires = null,
    [property: JsonPropertyName("license")]    string License = "",
    [property: JsonPropertyName("licenseUrl")] string? LicenseUrl = null,
    [property: JsonPropertyName("sourceUrl")]  string? SourceUrl = null,
    [property: JsonPropertyName("notice")]     string? Notice = null,
    // v3 V1: one or two player-facing sentences — what this dependency adds (shown in the install step).
    [property: JsonPropertyName("description")] string? Description = null);

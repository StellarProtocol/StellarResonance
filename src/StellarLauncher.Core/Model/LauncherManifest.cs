using System.Text.Json;
using System.Text.Json.Serialization;

namespace StellarLauncher.Core.Model;

public sealed record LauncherManifest(
    [property: JsonPropertyName("version")]    string Version,
    [property: JsonPropertyName("date")]       string Date,
    [property: JsonPropertyName("windowsUrl")] string WindowsUrl,
    [property: JsonPropertyName("linuxUrl")]   string LinuxUrl,
    [property: JsonPropertyName("notes")]      string? Notes,
    [property: JsonPropertyName("windowsSha256")] string? WindowsSha256 = null,
    [property: JsonPropertyName("linuxSha256")]   string? LinuxSha256 = null)
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>The download URL for the running OS.</summary>
    public string DownloadUrlFor(bool isWindows) => isWindows ? WindowsUrl : LinuxUrl;

    /// <summary>The expected SHA-256 of the download for the running OS, if published.</summary>
    public string? ShaFor(bool isWindows) => isWindows ? WindowsSha256 : LinuxSha256;
}

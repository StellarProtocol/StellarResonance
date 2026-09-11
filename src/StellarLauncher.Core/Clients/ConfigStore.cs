using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;

namespace StellarLauncher.Core.Clients;

/// <summary>settings.json v2. A v1 file (no "version") is imported once, kept as settings.v1.json,
/// and rewritten as v2. No legacy mirror is written (spec § 6).</summary>
public sealed class ConfigStore : IConfigStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IFileSystem _fs;
    private readonly IPlatformInfo _platform;
    private readonly string _backupPath;

    public string SettingsPath { get; }

    public ConfigStore(IFileSystem fs, IPlatformInfo platform)
    {
        _fs = fs; _platform = platform;
        var dir = _fs.Path.Combine(platform.AppDataDir, "stellar-launcher");
        SettingsPath = _fs.Path.Combine(dir, "settings.json");
        _backupPath = _fs.Path.Combine(dir, "settings.v1.json");
    }

    public LauncherConfig Load()
    {
        if (!_fs.File.Exists(SettingsPath)) return new LauncherConfig();
        var text = _fs.File.ReadAllText(SettingsPath);
        try
        {
            return IsV2(text)
                ? JsonSerializer.Deserialize<LauncherConfig>(text, Json) ?? new LauncherConfig()
                : ImportV1(text);
        }
        catch (JsonException)
        {
            return new LauncherConfig();   // corrupt → empty; the file is left for the user
        }
    }

    public void Save(LauncherConfig cfg)
    {
        _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(SettingsPath)!);
        _fs.File.WriteAllText(SettingsPath, JsonSerializer.Serialize(cfg, Json));
    }

    private static bool IsV2(string text)
    {
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("version", out var v)
            && v.ValueKind == JsonValueKind.Number && v.GetInt32() >= 2;
    }

    private LauncherConfig ImportV1(string text)
    {
        var v1 = JsonSerializer.Deserialize<LauncherSettings>(text, Json) ?? new LauncherSettings();
        var cfg = V1Import.Convert(v1, _platform.IsWindows);
        if (!_fs.File.Exists(_backupPath)) _fs.File.WriteAllText(_backupPath, text);
        Save(cfg);
        return cfg;
    }
}

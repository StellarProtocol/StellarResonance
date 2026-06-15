// src/StellarLauncher.Core/Services/SettingsStore.cs
using System.IO.Abstractions;
using System.Text.Json;
using StellarLauncher.Core.Platform;

namespace StellarLauncher.Core.Services;

public sealed class SettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly IFileSystem _fs;
    private readonly string _path;

    public SettingsStore(IFileSystem fs, IPlatformInfo platform)
    {
        _fs = fs;
        _path = _fs.Path.Combine(platform.AppDataDir, "stellar-launcher", "settings.json");
    }

    public LauncherSettings Load()
    {
        if (!_fs.File.Exists(_path)) return new LauncherSettings();
        try
        {
            return JsonSerializer.Deserialize<LauncherSettings>(_fs.File.ReadAllText(_path), Json)
                   ?? new LauncherSettings();
        }
        catch (JsonException)
        {
            return new LauncherSettings();  // corrupt file → fall back to defaults
        }
    }

    public void Save(LauncherSettings settings)
    {
        _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(_path)!);
        _fs.File.WriteAllText(_path, JsonSerializer.Serialize(settings, Json));
    }
}

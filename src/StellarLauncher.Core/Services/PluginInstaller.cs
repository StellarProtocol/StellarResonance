using System;
using System.IO;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace StellarLauncher.Core.Services;

public sealed class PluginInstaller : IPluginInstaller
{
    private const string VersionMarker = ".plugin-version";
    private readonly IFileSystem _fs;
    public PluginInstaller(IFileSystem fs) => _fs = fs;

    public async Task InstallAsync(Stream dll, string expectedSha256, string gameMiniDir,
        string pluginId, string dllFileName, string version, CancellationToken ct = default)
    {
        GuardName(pluginId, nameof(pluginId));
        GuardName(dllFileName, nameof(dllFileName));

        using var buffer = new MemoryStream();
        await dll.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(buffer, ct)).ToLowerInvariant();
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"plugin sha256 mismatch (expected {expectedSha256}, got {actual})");

        var dir = PluginDir(gameMiniDir, pluginId);
        _fs.Directory.CreateDirectory(dir);
        buffer.Position = 0;
        using (var outStream = _fs.File.Create(_fs.Path.Combine(dir, dllFileName)))
            await buffer.CopyToAsync(outStream, ct);
        _fs.File.WriteAllText(_fs.Path.Combine(dir, VersionMarker), version);
    }

    public void Remove(string gameMiniDir, string pluginId)
    {
        GuardName(pluginId, nameof(pluginId));
        var dir = PluginDir(gameMiniDir, pluginId);
        if (_fs.Directory.Exists(dir)) _fs.Directory.Delete(dir, recursive: true);
    }

    public bool IsInstalled(string gameMiniDir, string pluginId)
        => InstalledVersion(gameMiniDir, pluginId) is not null;

    public string? InstalledVersion(string gameMiniDir, string pluginId)
    {
        GuardName(pluginId, nameof(pluginId));
        var marker = _fs.Path.Combine(PluginDir(gameMiniDir, pluginId), VersionMarker);
        return _fs.File.Exists(marker) ? _fs.File.ReadAllText(marker).Trim() : null;
    }

    private string PluginDir(string gameMiniDir, string pluginId)
        => _fs.Path.Combine(gameMiniDir, "stellar", "plugins", pluginId);

    private static void GuardName(string value, string param)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Contains('/') || value.Contains('\\') || value.Contains(".."))
            throw new ArgumentException($"unsafe {param}: {value}", param);
    }
}

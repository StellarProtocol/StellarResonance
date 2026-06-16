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
        var target = _fs.Path.Combine(dir, dllFileName);
        // Drop any other copy of this DLL first (e.g. an old-launcher install in a differently-cased
        // folder) so the framework — which loads every *.dll under stellar/plugins by assembly name —
        // doesn't end up loading the plugin twice.
        RemoveDllCopies(gameMiniDir, dllFileName, keep: target);
        _fs.Directory.CreateDirectory(dir);
        buffer.Position = 0;
        using (var outStream = _fs.File.Create(target))
            await buffer.CopyToAsync(outStream, ct);
        _fs.File.WriteAllText(_fs.Path.Combine(dir, VersionMarker), version);
    }

    public void Remove(string gameMiniDir, string pluginId, string? dllFileName = null)
    {
        GuardName(pluginId, nameof(pluginId));
        var dir = PluginDir(gameMiniDir, pluginId);
        if (_fs.Directory.Exists(dir)) _fs.Directory.Delete(dir, recursive: true);
        // Also clear an adopted copy that lived in a different (e.g. old-launcher) folder.
        if (!string.IsNullOrEmpty(dllFileName)) RemoveDllCopies(gameMiniDir, dllFileName, keep: null);
    }

    public bool IsInstalled(string gameMiniDir, string pluginId)
        => InstalledVersion(gameMiniDir, pluginId) is not null;

    public string? InstalledVersion(string gameMiniDir, string pluginId)
    {
        GuardName(pluginId, nameof(pluginId));
        var marker = _fs.Path.Combine(PluginDir(gameMiniDir, pluginId), VersionMarker);
        return _fs.File.Exists(marker) ? _fs.File.ReadAllText(marker).Trim() : null;
    }

    /// <summary>Path of <paramref name="dllFileName"/> anywhere under stellar/plugins (case-insensitive),
    /// or null. Detects installs the launcher didn't create (no marker) — e.g. from a previous launcher.</summary>
    public string? FindInstalledDll(string gameMiniDir, string dllFileName)
    {
        var root = PluginsRoot(gameMiniDir);
        if (string.IsNullOrEmpty(dllFileName) || !_fs.Directory.Exists(root)) return null;
        foreach (var f in _fs.Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            if (string.Equals(_fs.Path.GetFileName(f), dllFileName, StringComparison.OrdinalIgnoreCase))
                return f;
        return null;
    }

    private void RemoveDllCopies(string gameMiniDir, string dllFileName, string? keep)
    {
        var root = PluginsRoot(gameMiniDir);
        if (!_fs.Directory.Exists(root)) return;
        var keepFull = keep is null ? null : _fs.Path.GetFullPath(keep);
        foreach (var f in _fs.Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            if (!string.Equals(_fs.Path.GetFileName(f), dllFileName, StringComparison.OrdinalIgnoreCase)) continue;
            if (keepFull is not null && string.Equals(_fs.Path.GetFullPath(f), keepFull, StringComparison.Ordinal)) continue;
            try
            {
                _fs.File.Delete(f);
                var d = _fs.Path.GetDirectoryName(f);
                if (d is not null && !string.Equals(d, root, StringComparison.Ordinal)
                    && _fs.Directory.Exists(d) && _fs.Directory.GetFileSystemEntries(d).Length == 0)
                    _fs.Directory.Delete(d);
            }
            catch { /* best effort */ }
        }
    }

    private string PluginsRoot(string gameMiniDir)
        => _fs.Path.Combine(gameMiniDir, "stellar", "plugins");

    private string PluginDir(string gameMiniDir, string pluginId)
        => _fs.Path.Combine(PluginsRoot(gameMiniDir), pluginId);

    private static void GuardName(string value, string param)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Contains('/') || value.Contains('\\') || value.Contains(".."))
            throw new ArgumentException($"unsafe {param}: {value}", param);
    }
}

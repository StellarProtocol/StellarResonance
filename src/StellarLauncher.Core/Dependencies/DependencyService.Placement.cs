using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Dependencies;

/// <summary>Download/verify/place mechanics for a single dependency (rules 2-5 of the dependency-service
/// contract). See <see cref="DependencyService.EnsureAsync"/> for the skip/requires handling around this.</summary>
public sealed partial class DependencyService
{
    private async Task<DependencyStatus> EnsureOneAsync(string gameMini, string pluginId, PluginDependency dep, CancellationToken ct)
    {
        var ledger = _store.Read(gameMini, pluginId);
        var existing = ledger.Entries.FirstOrDefault(e => e.DependencyId == dep.Id);
        if (IsInstalled(gameMini, existing, dep))
            return new DependencyStatus(dep.Id, DependencyState.Installed, null);

        var bytes = await DownloadCappedAsync(dep.Url, dep.Size, ct);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(hash, dep.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("checksum mismatch");

        var files = BuildFiles(gameMini, pluginId, dep, bytes);
        foreach (var f in files)
            if (IsForeign(gameMini, f.Abs))
                return new DependencyStatus(dep.Id, DependencyState.Blocked, DependencyPaths.Relative(gameMini, f.Abs));

        WriteAll(files);
        PruneStaleFiles(gameMini, existing, files.Select(f => DependencyPaths.Relative(gameMini, f.Abs)));

        var newEntry = new LedgerEntry(dep.Id, dep.Version, files.Select(f => new LedgerFile(
            DependencyPaths.Relative(gameMini, f.Abs), Convert.ToHexString(SHA256.HashData(f.Bytes)),
            dep.ModdedOnly && dep.Target == "game")).ToList());
        var entries = ledger.Entries.Where(e => e.DependencyId != dep.Id).Append(newEntry).ToList();
        _store.Write(gameMini, new DependencyLedger(pluginId, entries));

        return new DependencyStatus(dep.Id, DependencyState.Installed, null);
    }

    private bool IsInstalled(string gameMini, LedgerEntry? entry, PluginDependency dep)
    {
        if (entry is null || entry.Version != dep.Version) return false;
        foreach (var f in entry.Files)
        {
            var abs = _fs.Path.Combine(gameMini, f.Path);
            if (!_fs.File.Exists(abs)) return false;
            var hash = Convert.ToHexString(SHA256.HashData(_fs.File.ReadAllBytes(abs)));
            if (!string.Equals(hash, f.Sha256, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private async Task<byte[]> DownloadCappedAsync(string url, long cap, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int n;
        while ((n = await src.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + n > cap) throw new InvalidDataException("larger than declared");
            await buffer.WriteAsync(chunk.AsMemory(0, n), ct);
        }
        return buffer.ToArray();
    }

    private List<(string Abs, byte[] Bytes)> BuildFiles(string gameMini, string pluginId, PluginDependency dep, byte[] bytes)
    {
        if (dep.Kind != "zip")
        {
            var to = dep.Files[0].To;
            var abs = DependencyPaths.Resolve(gameMini, pluginId, dep.Target, to)
                      ?? throw new InvalidDataException($"path not allowed: {to}");
            return new List<(string, byte[])> { (abs, bytes) };
        }

        var result = new List<(string Abs, byte[] Bytes)>();
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/')) continue; // directory entry
            foreach (var mapping in dep.Files)
            {
                var dest = MapZipEntry(mapping, name);
                if (dest is null) continue;
                var abs = DependencyPaths.Resolve(gameMini, pluginId, dep.Target, dest)
                          ?? throw new InvalidDataException($"path not allowed: {dest}");
                using var es = entry.Open();
                using var buf = new MemoryStream();
                es.CopyTo(buf);
                result.Add((abs, buf.ToArray()));
            }
        }
        return result;
    }

    private static string? MapZipEntry(PluginDependencyFile mapping, string entryName)
    {
        if (mapping.From is null) return null;
        if (mapping.From.EndsWith('/'))
            return entryName.StartsWith(mapping.From, StringComparison.Ordinal) ? mapping.To + entryName[mapping.From.Length..] : null;
        return entryName == mapping.From ? mapping.To : null;
    }

    private bool IsForeign(string gameMini, string abs)
    {
        if (!_fs.File.Exists(abs)) return false;
        var rel = DependencyPaths.Relative(gameMini, abs);
        return !_store.ReadAll(gameMini).Any(l => l.Entries.Any(e => e.Files.Any(f =>
            string.Equals(f.Path, rel, StringComparison.OrdinalIgnoreCase))));
    }

    private void WriteAll(List<(string Abs, byte[] Bytes)> files)
    {
        foreach (var (abs, bytes) in files)
        {
            var dir = _fs.Path.GetDirectoryName(abs);
            if (!string.IsNullOrEmpty(dir)) _fs.Directory.CreateDirectory(dir);
            var tmp = abs + ".stellar-tmp";
            _fs.File.WriteAllBytes(tmp, bytes);
            _fs.File.Move(tmp, abs, overwrite: true);
        }
    }

    private void PruneStaleFiles(string gameMini, LedgerEntry? existing, IEnumerable<string> kept)
    {
        if (existing is null) return;
        var keptSet = new HashSet<string>(kept, StringComparer.OrdinalIgnoreCase);
        foreach (var old in existing.Files)
        {
            if (keptSet.Contains(old.Path)) continue;
            var abs = _fs.Path.Combine(gameMini, old.Path);
            if (_fs.File.Exists(abs)) _fs.File.Delete(abs);
        }
    }
}

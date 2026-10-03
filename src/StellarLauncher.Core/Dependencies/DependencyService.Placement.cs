using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Dependencies;

/// <summary>Download/verify/place mechanics for a single dependency (rules 2-5 of the dependency-service
/// contract, plus C1/I1's transactional placement). See <see cref="DependencyService.EnsureAsync"/> for
/// the skip/requires handling around this, <c>.Zip</c> for building the file list, and <c>.Ownership</c>
/// for the foreign/modified-file check.</summary>
public sealed partial class DependencyService
{
    private async Task<DependencyStatus> EnsureOneAsync(string gameMini, string pluginId, PluginDependency dep, CancellationToken ct)
    {
        var ledger = _store.Read(gameMini, pluginId);
        var existing = ledger.Entries.FirstOrDefault(e => e.DependencyId == dep.Id);
        if (IsInstalled(gameMini, existing, dep))
            return new DependencyStatus(dep.Id, DependencyState.Installed, null);

        var bytes = await DownloadCappedAsync(dep.Url, dep.Size, ct);
        if (!string.Equals(DependencyFileHash.Of(bytes), dep.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("checksum mismatch");

        var files = BuildFiles(gameMini, pluginId, dep, bytes);
        foreach (var f in files)
        {
            var blocked = CheckDestination(gameMini, pluginId, dep.Id, existing, f.Abs);
            if (blocked is not null) return blocked;
        }

        // C1/I1: placement is transactional — anything this attempt creates OR overwrites is rolled back
        // on any failure (a pre-existing destination is backed up to .stellar-bak before being replaced),
        // and the ledger is written only once every file is safely in place.
        var preExisting = new HashSet<string>(files.Select(f => f.Abs).Where(_fs.File.Exists), StringComparer.Ordinal);
        try
        {
            WriteAll(gameMini, pluginId, files);
            PruneStaleFiles(gameMini, pluginId, existing, files.Select(f => DependencyPaths.Relative(gameMini, f.Abs)));
            var newEntry = new LedgerEntry(dep.Id, dep.Version, files.Select(f => new LedgerFile(
                DependencyPaths.Relative(gameMini, f.Abs), DependencyFileHash.Of(f.Bytes),
                dep.ModdedOnly && dep.Target == "game")).ToList());
            var entries = ledger.Entries.Where(e => e.DependencyId != dep.Id).Append(newEntry).ToList();
            _store.Write(gameMini, new DependencyLedger(pluginId, entries));
            CleanupBackups(files);
        }
        catch
        {
            // Minor 4: a rollback failure must never replace the ORIGINAL exception as the reported cause.
            try { RollbackCreatedFiles(files, preExisting); RestoreBackups(files); }
            catch { /* best-effort — the exception below (the real cause) is what EnsureAsync reports */ }
            throw;
        }

        return new DependencyStatus(dep.Id, DependencyState.Installed, null);
    }

    /// <summary>C1: undoes everything THIS attempt newly created — a file that did not exist before (so is
    /// safe to remove) and any leftover <c>.stellar-tmp</c> — so a retry never self-blocks on its own debris.</summary>
    private void RollbackCreatedFiles(List<(string Abs, byte[] Bytes)> files, HashSet<string> preExisting)
    {
        foreach (var (abs, _) in files)
        {
            if (!preExisting.Contains(abs) && _fs.File.Exists(abs)) _fs.File.Delete(abs);
            var tmp = abs + ".stellar-tmp";
            if (_fs.File.Exists(tmp)) _fs.File.Delete(tmp);
        }
    }

    /// <summary>I1: restores every <c>.stellar-bak</c> this attempt took of a pre-existing destination, so
    /// a failed UPDATE leaves the previous version intact rather than half-overwritten.</summary>
    private void RestoreBackups(List<(string Abs, byte[] Bytes)> files)
    {
        foreach (var (abs, _) in files)
        {
            var bak = abs + ".stellar-bak";
            if (_fs.File.Exists(bak)) _fs.File.Move(bak, abs, overwrite: true);
        }
    }

    /// <summary>I1: on success, the backups this attempt took are no longer needed. Best-effort — a
    /// leftover backup is cosmetic debris, never a correctness problem, so it must not turn a successful
    /// install into a reported failure.</summary>
    private void CleanupBackups(List<(string Abs, byte[] Bytes)> files)
    {
        foreach (var (abs, _) in files)
        {
            var bak = abs + ".stellar-bak";
            try { if (_fs.File.Exists(bak)) _fs.File.Delete(bak); } catch { /* best effort */ }
        }
    }

    private bool IsInstalled(string gameMini, LedgerEntry? entry, PluginDependency dep)
    {
        if (entry is null || entry.Version != dep.Version) return false;
        foreach (var f in entry.Files)
        {
            var abs = DependencyPaths.FromLedger(gameMini, f.Path); // I2
            if (abs is null || !DependencyFileHash.Matches(_fs, abs, f.Sha256)) return false;
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

    /// <summary>I1: a pre-existing destination is backed up to <c>.stellar-bak</c> before being replaced.
    /// M12: a stale <c>.stellar-tmp</c> left by a previous crashed attempt is always safe to delete (the
    /// suffix is our own naming pattern), and the temp file is cleaned up again if the final move fails.
    /// M8: a freshly (re)written destination drops any stale parked copy, so a later Unpark can never
    /// restore outdated bytes over it.</summary>
    private void WriteAll(string gameMini, string pluginId, List<(string Abs, byte[] Bytes)> files)
    {
        foreach (var (abs, bytes) in files)
        {
            var dir = _fs.Path.GetDirectoryName(abs);
            if (!string.IsNullOrEmpty(dir)) _fs.Directory.CreateDirectory(dir);

            var bak = abs + ".stellar-bak";
            if (_fs.File.Exists(abs)) _fs.File.Move(abs, bak, overwrite: true); // I1

            var tmp = abs + ".stellar-tmp";
            if (_fs.File.Exists(tmp)) _fs.File.Delete(tmp);
            _fs.File.WriteAllBytes(tmp, bytes);
            try { _fs.File.Move(tmp, abs, overwrite: true); }
            catch { if (_fs.File.Exists(tmp)) _fs.File.Delete(tmp); throw; }

            var parked = DependencyPaths.ParkedPath(gameMini, pluginId, DependencyPaths.Relative(gameMini, abs));
            if (_fs.File.Exists(parked)) _fs.File.Delete(parked);
        }
    }

    /// <summary>I2: an old recorded path is only acted on once validated. I3: a file whose content no
    /// longer matches the recorded hash was replaced by someone else and is left alone.</summary>
    private void PruneStaleFiles(string gameMini, string pluginId, LedgerEntry? existing, IEnumerable<string> kept)
    {
        if (existing is null) return;
        var keptSet = new HashSet<string>(kept, StringComparer.Ordinal); // M11
        foreach (var old in existing.Files)
        {
            if (keptSet.Contains(old.Path)) continue;
            var abs = DependencyPaths.FromLedger(gameMini, old.Path);
            if (abs is null) continue;
            if (DependencyFileHash.Matches(_fs, abs, old.Sha256)) _fs.File.Delete(abs);
            var parked = DependencyPaths.ParkedPath(gameMini, pluginId, old.Path); // M8
            if (_fs.File.Exists(parked)) _fs.File.Delete(parked);
        }
    }
}

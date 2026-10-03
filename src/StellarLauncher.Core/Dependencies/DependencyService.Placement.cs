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
        // Important (round 6): a present-but-unreadable ledger must abort immediately — never silently
        // treated as empty, which could overwrite or delete whatever it actually holds.
        if (!_store.TryReadForWrite(gameMini, pluginId, out var ledger))
            throw new InvalidOperationException("dependency record could not be read");
        var existing = ledger.Entries.FirstOrDefault(e => e.DependencyId == dep.Id);
        if (IsInstalled(gameMini, existing, dep))
            return new DependencyStatus(dep.Id, DependencyState.Installed, null);

        byte[] bytes;
        List<(string Abs, byte[] Bytes)> files;
        try
        {
            bytes = await DownloadCappedAsync(dep.Url, dep.Size, ct);
            if (!string.Equals(DependencyFileHash.Of(bytes), dep.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("checksum mismatch");
            // Controller round: BuildFiles runs INSIDE this try now — an archive-shape error (archive too
            // large, path not allowed, duplicate destination, no files matched) is exactly as unrelated to
            // the ledger as a download/verify failure, so it must be returned directly here too, never left
            // to escape as an exception that EnsureAsync's catch-all would route through AnnotateIfCorrupt.
            files = BuildFiles(gameMini, pluginId, dep, bytes);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Minor 1 (round 6, narrowing round 5): a download/verify failure (HTTP error, checksum
            // mismatch, size cap) — or, as of the controller round, an archive-shape failure — has
            // nothing to do with the ledger — returned directly, rather than thrown, so EnsureAsync's
            // caller never runs it through AnnotateIfCorrupt.
            return new DependencyStatus(dep.Id, DependencyState.Failed, ex.Message);
        }

        foreach (var f in files)
        {
            // The "unreadable" note (if any) is applied once, uniformly, by EnsureAsync — not here.
            var blocked = CheckDestination(gameMini, pluginId, dep.Id, existing, f.Abs);
            if (blocked is not null) return blocked;
        }

        return CommitPlacement(gameMini, pluginId, dep, ledger, existing, files);
    }

    /// <summary>C1/I1: placement is transactional — anything this attempt creates OR overwrites is rolled
    /// back on any failure (a pre-existing destination is backed up to <c>.stellar-bak</c> before being
    /// replaced), and the ledger is written only once every file is safely in place.</summary>
    private DependencyStatus CommitPlacement(string gameMini, string pluginId, PluginDependency dep,
        DependencyLedger ledger, LedgerEntry? existing, List<(string Abs, byte[] Bytes)> files)
    {
        var preExisting = new HashSet<string>(files.Select(f => f.Abs).Where(_fs.File.Exists), StringComparer.Ordinal);
        var backedUp = new HashSet<string>(StringComparer.Ordinal); // minor 1: only paths THIS attempt backed up
        var parkedToClean = new HashSet<string>(StringComparer.Ordinal); // minor 3: collected, acted on after commit
        try
        {
            WriteAll(gameMini, files, backedUp, parkedToClean);
            PruneStaleFiles(gameMini, existing, files.Select(f => DependencyPaths.Relative(gameMini, f.Abs)), parkedToClean);
            var newEntry = new LedgerEntry(dep.Id, dep.Version, files.Select(f => new LedgerFile(
                DependencyPaths.Relative(gameMini, f.Abs), DependencyFileHash.Of(f.Bytes),
                dep.ModdedOnly && dep.Target == "game")).ToList());
            var entries = ledger.Entries.Where(e => e.DependencyId != dep.Id).Append(newEntry).ToList();
            _store.Write(gameMini, new DependencyLedger(pluginId, entries));
            CleanupParkedCopies(gameMini, pluginId, parkedToClean); // minor 3: only after the ledger write succeeds
            CleanupBackups(files, backedUp);
        }
        catch (Exception original)
        {
            // Minor 2: isolate per file AND per step, so one failed restore never skips the others.
            // Minor 4: when that leaves the rollback incomplete, say so — but the ORIGINAL exception
            // (never a rollback-raised one) is always what's preserved as the reported cause.
            var rollbackOk = RollbackAll(files, preExisting, backedUp);
            if (rollbackOk) throw;
            throw new InvalidOperationException($"{original.Message} (rollback incomplete)", original);
        }

        return new DependencyStatus(dep.Id, DependencyState.Installed, null);
    }

    /// <summary>Important 2(c), narrowed by minor 1 (round 6): the "dependency record was unreadable…"
    /// note explains a Blocked outcome, or a Failed outcome produced by PLACEMENT or LEDGER HANDLING —
    /// never a bare gate status ("requires … listed earlier", "waiting for …") and never a download/verify
    /// failure (HTTP error, checksum mismatch, size cap), since neither has anything to do with the
    /// ledger. Installed/Skipped/NotInstalled are fine outcomes and never carry it either — the
    /// <c>.corrupt</c> evidence is never auto-deleted, so annotating a fine or unrelated outcome would
    /// leave a scary, misleading note around forever. Derived from whether <c>&lt;id&gt;.json.corrupt</c>
    /// exists on disk right now — never from whether this call is the one that quarantined it — so the
    /// note appears (and keeps appearing) regardless of which earlier call discovered the corruption.
    /// Callers (<see cref="DependencyService.EnsureAsync"/>) apply this only to a Blocked result returned
    /// normally, or to a Failed status built from an exception that escaped placement/ledger-handling code
    /// — never to a gate status or a directly-returned download/verify Failed.</summary>
    private DependencyStatus AnnotateIfCorrupt(string gameMini, string pluginId, DependencyStatus status)
    {
        if (status.State is not (DependencyState.Blocked or DependencyState.Failed)) return status;
        if (!_fs.File.Exists(DependencyPaths.LedgerFile(gameMini, pluginId) + ".corrupt")) return status;
        var note = $"dependency record was unreadable; kept as {pluginId}.json.corrupt";
        return status with { Detail = status.Detail is null ? note : $"{status.Detail}; {note}" };
    }

    /// <summary>C1/I1, minor 2: undoes everything THIS attempt did — per file, per step, each isolated so
    /// one failure never stops the others — and reports whether every step actually succeeded.</summary>
    private bool RollbackAll(List<(string Abs, byte[] Bytes)> files, HashSet<string> preExisting, HashSet<string> backedUp)
    {
        var ok = true;
        foreach (var (abs, _) in files)
        {
            if (!TryDeleteCreated(abs, preExisting)) ok = false;
            if (!TryRestoreBackup(abs, backedUp)) ok = false;
        }
        return ok;
    }

    /// <summary>C1: deletes a file that did not exist before this attempt (so is safe to remove) and any
    /// leftover <c>.stellar-tmp</c> — so a retry never self-blocks on its own debris.</summary>
    private bool TryDeleteCreated(string abs, HashSet<string> preExisting)
    {
        try
        {
            if (!preExisting.Contains(abs) && _fs.File.Exists(abs)) _fs.File.Delete(abs);
            var tmp = abs + ".stellar-tmp";
            if (_fs.File.Exists(tmp)) _fs.File.Delete(tmp);
            return true;
        }
        catch { return false; }
    }

    /// <summary>I1, minor 1: restores THIS attempt's own <c>.stellar-bak</c> (never a stale one left by
    /// some earlier, unrelated attempt), so a failed UPDATE leaves the previous version intact.</summary>
    private bool TryRestoreBackup(string abs, HashSet<string> backedUp)
    {
        if (!backedUp.Contains(abs)) return true;
        try
        {
            var bak = abs + ".stellar-bak";
            if (_fs.File.Exists(bak)) _fs.File.Move(bak, abs, overwrite: true);
            return true;
        }
        catch { return false; }
    }

    /// <summary>I1: on success, the backups this attempt took are no longer needed. Best-effort — a
    /// leftover backup is cosmetic debris, never a correctness problem.</summary>
    private void CleanupBackups(List<(string Abs, byte[] Bytes)> files, HashSet<string> backedUp)
    {
        foreach (var (abs, _) in files)
        {
            if (!backedUp.Contains(abs)) continue;
            var bak = abs + ".stellar-bak";
            try { if (_fs.File.Exists(bak)) _fs.File.Delete(bak); } catch { /* best effort */ }
        }
    }

    /// <summary>Minor 3: run only after the ledger write has committed, so a failure before that point
    /// never destroys a parked copy the (aborted) attempt had no right to touch yet. Best-effort — this
    /// must never turn an already-successful install into a reported failure.</summary>
    private void CleanupParkedCopies(string gameMini, string pluginId, HashSet<string> relativePaths)
    {
        foreach (var rel in relativePaths)
        {
            var parked = DependencyPaths.ParkedPath(gameMini, pluginId, rel);
            try { if (_fs.File.Exists(parked)) _fs.File.Delete(parked); } catch { /* best effort */ }
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
    /// Minor 1 (round 3): a stale leftover backup is cleared first, same as the <c>.stellar-tmp</c>
    /// handling below. Minor 2 (round 4): a stale backup is cleared even when there's nothing to back up
    /// this time (the destination is currently absent). Minor 3 (round 3): a destination's parked copy is
    /// only COLLECTED here — <see cref="CleanupParkedCopies"/> deletes it later, once the ledger write has
    /// actually committed.</summary>
    private void WriteAll(string gameMini, List<(string Abs, byte[] Bytes)> files, HashSet<string> backedUp, HashSet<string> parkedToClean)
    {
        foreach (var (abs, bytes) in files)
        {
            var dir = _fs.Path.GetDirectoryName(abs);
            if (!string.IsNullOrEmpty(dir)) _fs.Directory.CreateDirectory(dir);

            var bak = abs + ".stellar-bak";
            if (_fs.File.Exists(abs))
            {
                if (_fs.File.Exists(bak)) _fs.File.Delete(bak); // minor 1 (round 3)
                _fs.File.Move(abs, bak, overwrite: true);
                backedUp.Add(abs);
            }
            else if (_fs.File.Exists(bak))
            {
                _fs.File.Delete(bak); // minor 2 (round 4): stale debris cleared even with nothing to back up
            }

            var tmp = abs + ".stellar-tmp";
            if (_fs.File.Exists(tmp)) _fs.File.Delete(tmp);
            _fs.File.WriteAllBytes(tmp, bytes);
            try { _fs.File.Move(tmp, abs, overwrite: true); }
            catch { if (_fs.File.Exists(tmp)) _fs.File.Delete(tmp); throw; }

            parkedToClean.Add(DependencyPaths.Relative(gameMini, abs)); // minor 3
        }
    }

    /// <summary>I2: an old recorded path is only acted on once validated. I3: a file whose content no
    /// longer matches the recorded hash was replaced by someone else and is left alone. Minor 3: a pruned
    /// destination's parked copy is only COLLECTED here, deleted later via <see cref="CleanupParkedCopies"/>.</summary>
    private void PruneStaleFiles(string gameMini, LedgerEntry? existing, IEnumerable<string> kept, HashSet<string> parkedToClean)
    {
        if (existing is null) return;
        var keptSet = new HashSet<string>(kept, StringComparer.Ordinal); // M11
        foreach (var old in existing.Files)
        {
            if (keptSet.Contains(old.Path)) continue;
            var abs = DependencyPaths.FromLedger(gameMini, old.Path);
            if (abs is null) continue;
            if (DependencyFileHash.Matches(_fs, abs, old.Sha256)) _fs.File.Delete(abs);
            parkedToClean.Add(old.Path); // minor 3 (M8)
        }
    }
}

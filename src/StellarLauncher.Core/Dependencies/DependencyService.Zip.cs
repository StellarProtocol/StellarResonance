using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Dependencies;

/// <summary>Builds the (destination, bytes) list for a single dependency: a plain file copies straight
/// through; a zip is walked entry-by-entry and mapped through <see cref="PluginDependency.Files"/>
/// (M9/M10/M13 guard the result; I7 is enforced by <see cref="DependencyPaths.Resolve"/> rejecting any
/// mapped destination that escapes).</summary>
public sealed partial class DependencyService
{
    private const long MinZipCap = 64L * 1024 * 1024;

    private List<(string Abs, byte[] Bytes)> BuildFiles(string gameMini, string pluginId, PluginDependency dep, byte[] bytes)
    {
        var files = dep.Kind == "zip" ? BuildZipKind(gameMini, pluginId, dep, bytes) : BuildFileKind(gameMini, pluginId, dep, bytes);
        var seen = new HashSet<string>(StringComparer.Ordinal); // M11
        foreach (var f in files)
            if (!seen.Add(f.Abs))
                throw new InvalidDataException($"duplicate destination {DependencyPaths.Relative(gameMini, f.Abs)}"); // M10
        return files;
    }

    private static List<(string Abs, byte[] Bytes)> BuildFileKind(string gameMini, string pluginId, PluginDependency dep, byte[] bytes)
    {
        var to = dep.Files[0].To;
        var abs = DependencyPaths.Resolve(gameMini, pluginId, dep.Target, to)
                  ?? throw new InvalidDataException($"path not allowed: {to}");
        return new List<(string, byte[])> { (abs, bytes) };
    }

    private static List<(string Abs, byte[] Bytes)> BuildZipKind(string gameMini, string pluginId, PluginDependency dep, byte[] bytes)
    {
        var cap = Math.Max(dep.Size * 4, MinZipCap); // M13
        long total = 0;
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
                          ?? throw new InvalidDataException($"path not allowed: {dest}"); // I7
                using var es = entry.Open();
                var data = ReadCapped(es, cap - total);
                total += data.Length;
                result.Add((abs, data));
            }
        }
        if (result.Count == 0) throw new InvalidDataException("no files matched"); // M9
        return result;
    }

    /// <summary>Streams out of <paramref name="s"/> in chunks, never buffering past <paramref name="remaining"/>
    /// — a zip-bomb entry is rejected mid-read instead of being fully decompressed into memory first.</summary>
    private static byte[] ReadCapped(Stream s, long remaining)
    {
        using var buf = new MemoryStream();
        var chunk = new byte[81920];
        int n;
        while ((n = s.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buf.Length + n > remaining) throw new InvalidDataException("archive too large");
            buf.Write(chunk, 0, n);
        }
        return buf.ToArray();
    }

    private static string? MapZipEntry(PluginDependencyFile mapping, string entryName)
    {
        if (mapping.From is null) return null;
        if (mapping.From.EndsWith('/'))
            return entryName.StartsWith(mapping.From, StringComparison.Ordinal) ? mapping.To + entryName[mapping.From.Length..] : null;
        return entryName == mapping.From ? mapping.To : null;
    }
}

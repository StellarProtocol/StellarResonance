using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>Zip edge cases from the Task 4 review round: I7 (escape), M9 (no match), M10 (duplicate
/// destination), M13 (decompression cap). Shares helpers/fields with <see cref="DependencyServiceTests"/>.</summary>
public sealed partial class DependencyServiceTests
{
    // I7: a mapping whose remainder climbs out of its prefix must be rejected by DependencyPaths.Resolve,
    // and nothing from the archive may be written.
    [Fact]
    public async Task Zip_entry_escaping_its_prefix_is_rejected_and_nothing_is_written()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        using (var w = new StreamWriter(zip.CreateEntry("pack/../../BepInEx/evil.dll").Open())) w.Write("E");
        var bytes = ms.ToArray();
        _web["https://cdn/evil"] = bytes;
        var d = new PluginDependency("evil", "evil", "1", "https://cdn/evil", Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, "zip",
            new[] { new PluginDependencyFile("pack/", "out/") }, "game");

        var st = Assert.Single(await Make().EnsureAsync(G, "p", new[] { d }, None, default));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.False(_fs.File.Exists("/game_mini/BepInEx/evil.dll"));
        Assert.False(_fs.File.Exists("/game_mini/out/evil.dll"));
    }

    // M9: a zip whose mappings match no entry at all must fail explicitly rather than silently install nothing.
    [Fact]
    public async Task Zip_with_no_matching_entries_fails()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        using (var w = new StreamWriter(zip.CreateEntry("other/thing.txt").Open())) w.Write("x");
        var bytes = ms.ToArray();
        _web["https://cdn/nomatch"] = bytes;
        var d = new PluginDependency("nomatch", "nomatch", "1", "https://cdn/nomatch", Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, "zip",
            new[] { new PluginDependencyFile("pack/", "out/") }, "plugin");

        var st = Assert.Single(await Make().EnsureAsync(G, "p", new[] { d }, None, default));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.Equal("no files matched", st.Detail);
    }

    // M10: two mappings landing on the same destination must fail rather than silently let one clobber the other.
    [Fact]
    public async Task Zip_mappings_producing_a_duplicate_destination_fail()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("a/x.fx").Open())) w.Write("A");
            using (var w = new StreamWriter(zip.CreateEntry("b/x.fx").Open())) w.Write("B");
        }
        var bytes = ms.ToArray();
        _web["https://cdn/dup"] = bytes;
        var d = new PluginDependency("dup", "dup", "1", "https://cdn/dup", Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, "zip",
            new[] { new PluginDependencyFile("a/", "out/"), new PluginDependencyFile("b/", "out/") }, "plugin");

        var st = Assert.Single(await Make().EnsureAsync(G, "p", new[] { d }, None, default));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.Equal("duplicate destination stellar/deps/p/out/x.fx", st.Detail);
        Assert.False(_fs.File.Exists("/game_mini/stellar/deps/p/out/x.fx"));
    }

    // M13: the decompressed total is capped at max(4x declared, 64 MiB) — a highly-compressible entry
    // that blows past that must fail before the whole thing is held in memory.
    [Fact]
    public async Task Zip_decompressed_beyond_the_cap_fails()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("pack/huge.bin", CompressionLevel.Optimal);
            using var es = entry.Open();
            var chunk = new byte[1024 * 1024]; // 1 MiB of zeros
            for (var i = 0; i < 70; i++) es.Write(chunk, 0, chunk.Length); // 70 MiB decompressed, tiny compressed
        }
        var bytes = ms.ToArray();
        _web["https://cdn/bomb"] = bytes;
        var d = new PluginDependency("bomb", "bomb", "1", "https://cdn/bomb", Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, "zip",
            new[] { new PluginDependencyFile("pack/", "out/") }, "plugin");

        var st = Assert.Single(await Make().EnsureAsync(G, "p", new[] { d }, None, default));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.Equal("archive too large", st.Detail);
        Assert.False(_fs.File.Exists("/game_mini/stellar/deps/p/out/huge.bin"));
    }
}

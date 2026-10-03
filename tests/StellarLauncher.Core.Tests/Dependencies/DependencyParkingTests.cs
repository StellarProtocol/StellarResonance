using System;
using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using StellarLauncher.Core.Dependencies;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

public sealed class DependencyParkingTests
{
    private const string G = "/game_mini";
    private static readonly byte[] DxgiBytes = { 1 };
    private static readonly byte[] PlainBytes = { 2 };
    private static string DxgiHash => Convert.ToHexString(SHA256.HashData(DxgiBytes));
    private static string PlainHash => Convert.ToHexString(SHA256.HashData(PlainBytes));

    private static (MockFileSystem fs, DependencyService s) Setup()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/game_mini/dxgi.dll", new MockFileData(DxgiBytes));
        fs.AddFile("/game_mini/plain.dll", new MockFileData(PlainBytes));
        new DependencyLedgerStore(fs).Write(G, new DependencyLedger("p", new[] { new LedgerEntry("fx", "1", new[]
        {
            new LedgerFile("dxgi.dll", DxgiHash, true), new LedgerFile("plain.dll", PlainHash, false),
        }) }));
        return (fs, new DependencyService(fs, new System.Net.Http.HttpClient()));
    }

    [Fact]
    public void Park_moves_only_modded_only_files_and_unpark_restores()
    {
        var (fs, s) = Setup();
        s.ParkModdedOnly(G);
        Assert.False(fs.File.Exists("/game_mini/dxgi.dll"));
        Assert.True(fs.File.Exists("/game_mini/stellar/deps-parked/p/dxgi.dll"));
        Assert.True(fs.File.Exists("/game_mini/plain.dll"));
        s.ParkModdedOnly(G);   // idempotent
        s.UnparkModdedOnly(G);
        Assert.True(fs.File.Exists("/game_mini/dxgi.dll"));
        s.UnparkModdedOnly(G); // idempotent
        Assert.True(fs.File.Exists("/game_mini/dxgi.dll"));
    }

    [Fact]
    public void Unpark_never_overwrites_a_file_that_appeared_meanwhile()
    {
        var (fs, s) = Setup();
        s.ParkModdedOnly(G);
        fs.AddFile("/game_mini/dxgi.dll", new MockFileData(new byte[] { 7 }));
        s.UnparkModdedOnly(G);
        Assert.Equal(new byte[] { 7 }, fs.File.ReadAllBytes("/game_mini/dxgi.dll"));
        Assert.True(fs.File.Exists("/game_mini/stellar/deps-parked/p/dxgi.dll"));
    }

    // I3: Park must not move a file whose live content no longer matches the recorded hash — the user or
    // another tool replaced it, so parking it (and later restoring possibly-stale bytes) would be wrong.
    [Fact]
    public void Park_leaves_a_user_replaced_moddedOnly_file_live()
    {
        var (fs, s) = Setup();
        fs.File.WriteAllBytes("/game_mini/dxgi.dll", new byte[] { 42 }); // replaced before any park ever ran

        s.ParkModdedOnly(G);

        Assert.True(fs.File.Exists("/game_mini/dxgi.dll"));
        Assert.Equal(new byte[] { 42 }, fs.File.ReadAllBytes("/game_mini/dxgi.dll"));
        Assert.False(fs.File.Exists("/game_mini/stellar/deps-parked/p/dxgi.dll"));
    }

    // I2: a ledger entry recording an unsafe path must never be resolved to an absolute path, by either
    // Park or Unpark, let alone acted on — a file planted at the would-be escape target is left untouched.
    [Fact]
    public void Park_and_unpark_ignore_unsafe_ledger_paths_and_never_touch_planted_targets()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(G);
        fs.AddFile("/x", new MockFileData("outside-x"));
        fs.AddFile("/abs/x", new MockFileData("outside-abs"));
        new DependencyLedgerStore(fs).Write(G, new DependencyLedger("p", new[] { new LedgerEntry("evil", "1", new[]
        {
            new LedgerFile("../x", "h1", true),
            new LedgerFile("/abs/x", "h2", true),
            new LedgerFile("C:/x", "h3", true),
        }) }));
        var s = new DependencyService(fs, new System.Net.Http.HttpClient());

        s.ParkModdedOnly(G);
        s.UnparkModdedOnly(G);

        Assert.Equal("outside-x", fs.File.ReadAllText("/x"));
        Assert.Equal("outside-abs", fs.File.ReadAllText("/abs/x"));
    }

    // M2: a malformed ledger next to a valid one must not stop Park from processing the valid one.
    [Fact]
    public void A_corrupt_ledger_file_does_not_block_parking_the_rest()
    {
        var (fs, s) = Setup();
        fs.AddFile("/game_mini/stellar/deps/broken.json", new MockFileData("{ not valid json"));

        s.ParkModdedOnly(G); // must not throw

        Assert.False(fs.File.Exists("/game_mini/dxgi.dll"));
        Assert.True(fs.File.Exists("/game_mini/stellar/deps-parked/p/dxgi.dll"));
    }

    // Important 1: a ledger file that parses but has the wrong shape (e.g. a missing "Entries" field,
    // which System.Text.Json fills with null regardless of the record's non-nullable annotation) must not
    // crash Park/Unpark/ReadAll with a NullReferenceException — it's just as unreadable as a parse failure.
    [Fact]
    public void A_shape_invalid_ledger_file_does_not_block_park_unpark_or_readall()
    {
        var (fs, s) = Setup();
        fs.AddFile("/game_mini/stellar/deps/bad.json", new MockFileData("{\"PluginId\":\"x\"}"));

        var all = new DependencyLedgerStore(fs).ReadAll(G); // must not throw
        Assert.Single(all); // only "p" — "bad" is unreadable

        s.ParkModdedOnly(G); // must not throw
        Assert.True(fs.File.Exists("/game_mini/stellar/deps-parked/p/dxgi.dll"));

        s.UnparkModdedOnly(G); // must not throw
        Assert.True(fs.File.Exists("/game_mini/dxgi.dll"));
    }
}

using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.Core.Dependencies;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

public sealed class DependencyParkingTests
{
    private const string G = "/game_mini";

    private static (MockFileSystem fs, DependencyService s) Setup()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/game_mini/dxgi.dll", new MockFileData(new byte[] { 1 }));
        fs.AddFile("/game_mini/plain.dll", new MockFileData(new byte[] { 2 }));
        new DependencyLedgerStore(fs).Write(G, new DependencyLedger("p", new[] { new LedgerEntry("fx", "1", new[]
        {
            new LedgerFile("dxgi.dll", "x", true), new LedgerFile("plain.dll", "y", false),
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
}

using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.Core.Dependencies;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

public sealed class DependencyLedgerStoreTests
{
    [Fact]
    public void Write_then_read_round_trips_and_missing_reads_empty()
    {
        var fs = new MockFileSystem(); fs.AddDirectory("/game_mini");
        var store = new DependencyLedgerStore(fs);
        Assert.Empty(store.Read("/game_mini", "p").Entries);
        var ledger = new DependencyLedger("p", new[] { new LedgerEntry("fx", "1.0", new[] { new LedgerFile("dxgi.dll", "ab", true) }) });
        store.Write("/game_mini", ledger);
        var back = store.Read("/game_mini", "p");
        Assert.Equal("dxgi.dll", Assert.Single(Assert.Single(back.Entries).Files).Path);
        Assert.Single(store.ReadAll("/game_mini"));
    }

    [Fact]
    public void Empty_ledger_deletes_the_file()
    {
        var fs = new MockFileSystem(); fs.AddDirectory("/game_mini");
        var store = new DependencyLedgerStore(fs);
        store.Write("/game_mini", new DependencyLedger("p", new[] { new LedgerEntry("fx", "1", new LedgerFile[0]) }));
        store.Write("/game_mini", new DependencyLedger("p", new LedgerEntry[0]));
        Assert.False(fs.File.Exists("/game_mini/stellar/deps/p.json"));
    }
}

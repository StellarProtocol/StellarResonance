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

    // I2: the ledger's PluginId must always come from the FILE NAME, never from the JSON body — a tampered
    // or copy-pasted ledger file must not be able to make Read/ReadAll misreport which plugin owns it.
    [Fact]
    public void Read_always_uses_the_filename_derived_pluginId_even_if_the_json_claims_another()
    {
        var fs = new MockFileSystem(); fs.AddDirectory("/game_mini");
        var store = new DependencyLedgerStore(fs);
        store.Write("/game_mini", new DependencyLedger("p", new[] { new LedgerEntry("fx", "1", new[] { new LedgerFile("a.dll", "ab", false) }) }));
        var tampered = fs.File.ReadAllText("/game_mini/stellar/deps/p.json").Replace("\"PluginId\": \"p\"", "\"PluginId\": \"other\"");
        fs.File.WriteAllText("/game_mini/stellar/deps/p.json", tampered);

        Assert.Equal("p", store.Read("/game_mini", "p").PluginId);
        Assert.Equal("p", Assert.Single(store.ReadAll("/game_mini")).PluginId);
    }
}

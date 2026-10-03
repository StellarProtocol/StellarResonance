using System;
using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
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

    // M2: one corrupt ledger must not break everyone — Read/ReadAll must not throw, and the valid ledgers
    // next to it must still come back.
    [Fact]
    public void ReadAll_skips_a_ledger_file_that_fails_to_parse()
    {
        var fs = new MockFileSystem(); fs.AddDirectory("/game_mini");
        var store = new DependencyLedgerStore(fs);
        store.Write("/game_mini", new DependencyLedger("good", new[] { new LedgerEntry("fx", "1", new LedgerFile[0]) }));
        fs.AddFile("/game_mini/stellar/deps/broken.json", new MockFileData("{ not valid json"));

        var all = store.ReadAll("/game_mini");

        Assert.Single(all);
        Assert.Equal("good", all[0].PluginId);
        Assert.Empty(store.Read("/game_mini", "broken").Entries); // reads as empty rather than throwing
    }

    // M3: a ledger file's stem must look like a plugin id — this one is rejected outright by ReadAll
    // (never even deserialized), so it can never be trusted as a parked-path segment either.
    [Fact]
    public void ReadAll_ignores_a_ledger_file_whose_stem_is_not_a_valid_plugin_id()
    {
        var fs = new MockFileSystem(); fs.AddDirectory("/game_mini");
        var store = new DependencyLedgerStore(fs);
        store.Write("/game_mini", new DependencyLedger("bad id", new[] { new LedgerEntry("fx", "1", new[]
        {
            new LedgerFile("dxgi.dll", Convert.ToHexString(SHA256.HashData(new byte[] { 1 })), true),
        }) }));

        Assert.Empty(store.ReadAll("/game_mini"));
    }
}

using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using System.Text.Json;
using StellarLauncher.Core.Dependencies;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

public sealed class DependencyLedgerStoreTests
{
    private static readonly LedgerEntry[] FxEntry = { new("fx", "1.0", new[] { new LedgerFile("dxgi.dll", "ab", true) }) };

    // v3 V2/V3: the two flags round-trip, and a ledger without them is byte-shaped exactly as before (additive).
    [Fact]
    public void Kept_and_reinstall_flags_round_trip_and_are_absent_from_the_json_while_false()
    {
        var fs = new MockFileSystem(); fs.AddDirectory("/game_mini");
        var store = new DependencyLedgerStore(fs);
        store.Write("/game_mini", new DependencyLedger("p", FxEntry));
        var plain = fs.File.ReadAllText("/game_mini/stellar/deps/p.json");
        Assert.DoesNotContain("Kept", plain);
        Assert.DoesNotContain("ReinstallRequested", plain);

        store.Write("/game_mini", new DependencyLedger("p", FxEntry, Kept: true, ReinstallRequested: true));
        var back = store.Read("/game_mini", "p");
        Assert.True(back.Kept);
        Assert.True(back.ReinstallRequested);
    }

    [Fact]
    public void A_ledger_written_by_the_previous_launcher_reads_both_flags_as_false()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/game_mini/stellar/deps/p.json", new MockFileData(
            """{"PluginId":"p","Entries":[{"DependencyId":"fx","Version":"1.0","Files":[{"Path":"dxgi.dll","Sha256":"ab","ModdedOnly":true}]}]}"""));
        var back = new DependencyLedgerStore(fs).Read("/game_mini", "p");
        Assert.Equal("fx", Assert.Single(back.Entries).DependencyId);
        Assert.False(back.Kept);
        Assert.False(back.ReinstallRequested);
    }

    /// <summary>The previous launcher's ledger record (no flags) — what an older launcher deserializes into.</summary>
    private sealed record PreviousLedger(string PluginId, IReadOnlyList<LedgerEntry> Entries, IReadOnlyList<LedgerEntry>? Pending = null);

    // "Older launchers still read the ledger": System.Text.Json skips the unknown flag properties.
    [Fact]
    public void A_flagged_ledger_still_reads_in_the_previous_launchers_shape()
    {
        var fs = new MockFileSystem(); fs.AddDirectory("/game_mini");
        new DependencyLedgerStore(fs).Write("/game_mini", new DependencyLedger("p", FxEntry, Kept: true, ReinstallRequested: true));
        var old = JsonSerializer.Deserialize<PreviousLedger>(fs.File.ReadAllText("/game_mini/stellar/deps/p.json"));
        Assert.Equal("fx", Assert.Single(old!.Entries).DependencyId);
    }
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

    // Important 1: a ledger that PARSES fine but has the wrong shape (System.Text.Json fills a missing
    // field with null regardless of the record's non-nullable annotation) must still read as unreadable,
    // not crash some later consumer with a NullReferenceException.
    [Fact]
    public void ReadAll_skips_a_ledger_that_parses_but_has_the_wrong_shape()
    {
        var fs = new MockFileSystem(); fs.AddDirectory("/game_mini");
        var store = new DependencyLedgerStore(fs);
        store.Write("/game_mini", new DependencyLedger("good", new[] { new LedgerEntry("fx", "1", new LedgerFile[0]) }));
        fs.AddFile("/game_mini/stellar/deps/bad.json", new MockFileData("{\"PluginId\":\"x\"}")); // Entries deserializes to null

        var all = store.ReadAll("/game_mini"); // must not throw

        Assert.Single(all);
        Assert.Equal("good", all[0].PluginId);
        Assert.Empty(store.Read("/game_mini", "bad").Entries);
    }

    // Important 2(a)/(b): a corrupt ledger is quarantined (renamed, never deleted) rather than thrown
    // away, and the next successful Write to that plugin id must not destroy that evidence.
    [Fact]
    public void A_corrupt_ledger_is_quarantined_and_survives_the_next_write()
    {
        var fs = new MockFileSystem(); fs.AddDirectory("/game_mini");
        var store = new DependencyLedgerStore(fs);
        fs.AddFile("/game_mini/stellar/deps/p.json", new MockFileData("{ not valid json"));

        Assert.Empty(store.Read("/game_mini", "p").Entries); // read as empty, not thrown
        Assert.True(fs.File.Exists("/game_mini/stellar/deps/p.json.corrupt"));
        Assert.False(fs.File.Exists("/game_mini/stellar/deps/p.json"));

        store.Write("/game_mini", new DependencyLedger("p", new[] { new LedgerEntry("fx", "1", new LedgerFile[0]) }));

        Assert.True(fs.File.Exists("/game_mini/stellar/deps/p.json")); // the fresh write landed
        Assert.True(fs.File.Exists("/game_mini/stellar/deps/p.json.corrupt")); // evidence still there
    }

    // Important 2(a): the write is atomic — no .tmp artifact survives a normal write.
    [Fact]
    public void Write_leaves_no_temp_file_behind()
    {
        var fs = new MockFileSystem(); fs.AddDirectory("/game_mini");
        new DependencyLedgerStore(fs).Write("/game_mini", new DependencyLedger("p", new[] { new LedgerEntry("fx", "1", new LedgerFile[0]) }));

        Assert.False(fs.File.Exists("/game_mini/stellar/deps/p.json.tmp"));
        Assert.True(fs.File.Exists("/game_mini/stellar/deps/p.json"));
    }

    // Important 1 (round 4): an IOException while reading is transient/environmental, not evidence the
    // ledger's CONTENT is corrupt — it must not quarantine a perfectly valid file.
    [Fact]
    public void An_IOException_while_reading_does_not_quarantine_a_valid_ledger()
    {
        var fs = new MockFileSystem(); fs.AddDirectory("/game_mini");
        new DependencyLedgerStore(fs).Write("/game_mini", new DependencyLedger("p", new[] { new LedgerEntry("fx", "1", new LedgerFile[0]) }));

        var ledgerPath = "/game_mini/stellar/deps/p.json";
        var faulty = new FaultInjectingFileSystem(fs, ledgerPath, "ReadAllText"); // fires once
        var store = new DependencyLedgerStore(faulty);

        var unreadableThisPass = store.Read("/game_mini", "p");

        Assert.Empty(unreadableThisPass.Entries); // unreadable for this call only
        Assert.True(fs.File.Exists(ledgerPath)); // still in place, never renamed
        Assert.False(fs.File.Exists(ledgerPath + ".corrupt"));

        var retried = store.Read("/game_mini", "p"); // the fault already fired once — this read succeeds
        Assert.Single(retried.Entries);
    }
}

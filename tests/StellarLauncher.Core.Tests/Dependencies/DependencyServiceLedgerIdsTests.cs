using System.Net.Http;
using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.Core.Dependencies;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>Task 6 (b): LedgerPluginIds lists the plugins that own a ledger, so the pre-launch review can
/// sweep ledgers whose plugin is gone. It must list valid ledger stems only and read nothing.</summary>
public sealed class DependencyServiceLedgerIdsTests
{
    private const string G = "/game_mini";

    [Fact]
    public void No_ledger_folder_lists_nothing()
    {
        var fs = new MockFileSystem(); fs.AddDirectory(G);
        Assert.Empty(new DependencyService(fs, new HttpClient()).LedgerPluginIds(G));
    }

    [Fact]
    public void Lists_valid_ledger_stems_only_and_ignores_tmp_corrupt_and_folders()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{G}/stellar/deps/photostudio.json", new MockFileData("{}"));
        fs.AddFile($"{G}/stellar/deps/combatmeter.json", new MockFileData("not json at all"));
        fs.AddFile($"{G}/stellar/deps/old.json.corrupt", new MockFileData("x"));
        fs.AddFile($"{G}/stellar/deps/half.json.tmp", new MockFileData("x"));
        fs.AddFile($"{G}/stellar/deps/bad id.json", new MockFileData("{}"));
        fs.AddFile($"{G}/stellar/deps/photostudio/fx.dll", new MockFileData("x"));

        var ids = new DependencyService(fs, new HttpClient()).LedgerPluginIds(G);

        Assert.Equal(new[] { "combatmeter", "photostudio" }, ids);
        // Read-only: a corrupt ledger is listed but never quarantined by listing it.
        Assert.True(fs.File.Exists($"{G}/stellar/deps/combatmeter.json"));
        Assert.False(fs.File.Exists($"{G}/stellar/deps/combatmeter.json.corrupt"));
    }
}

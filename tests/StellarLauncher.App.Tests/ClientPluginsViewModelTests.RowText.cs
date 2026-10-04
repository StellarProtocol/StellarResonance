using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.Core.Dependencies;
using Xunit;

/// <summary>Final review M-d: the plugin page's row text for a dependency blocked by another plugin's file, and for
/// one waiting on a blocked prerequisite (split out to keep the main test file from growing).</summary>
public partial class ClientPluginsViewModelTests
{
    // Final review M-d: a destination recorded by ANOTHER plugin's ledger is named as such, not as the player's.
    [Fact]
    public async Task Another_plugins_file_at_the_destination_says_so_on_the_page()
    {
        var (f, vm, item) = await OpenWithGameDeps(GameDep("fx", "dxgi.dll"));
        f.Fs.AddFile($"{Test}/dxgi.dll", new MockFileData("theirs"));
        new DependencyLedgerStore(f.Fs).Write(Test, new DependencyLedger("other", new[] { new LedgerEntry("x", "1",
            new[] { new LedgerFile("dxgi.dll", new string('b', 64), false) }) }));

        vm.OpenPluginCommand.Execute(item);
        await item.DependencyRefresh;

        Assert.Equal("Blocked — another plugin's file is in the way: dxgi.dll", item.Dependencies.Single().StateText);
    }

    // Final review M-d: a dependency whose prerequisite is Blocked WAITS for it (by name) and stays ticked —
    // the player never skipped it.
    [Fact]
    public async Task A_dependent_of_a_blocked_prerequisite_shows_waiting_for_it_and_stays_ticked()
    {
        var bridge = GameDep("bridge", "bridge.dll") with { Optional = true, Requires = new[] { "fx" } };
        var (f, vm, item) = await OpenWithGameDeps(GameDep("fx", "dxgi.dll"), bridge);
        f.Fs.AddFile($"{Test}/dxgi.dll", new MockFileData("player's own"));

        vm.OpenPluginCommand.Execute(item);
        await item.DependencyRefresh;

        var row = item.Dependencies.Single(r => r.Id == "bridge");
        Assert.Equal("Waiting for fx name", row.StateText);
        Assert.True(row.Use);
    }
}

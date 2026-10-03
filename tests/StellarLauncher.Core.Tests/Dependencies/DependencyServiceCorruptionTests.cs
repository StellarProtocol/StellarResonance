using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using StellarLauncher.Core.Dependencies;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>Important 2(c): when a plugin's own ledger is found unreadable during EnsureAsync, the
/// resulting status (Blocked or Installed) carries "dependency record was unreadable; kept as
/// &lt;id&gt;.json.corrupt". Shares helpers/fields with <see cref="DependencyServiceTests"/>.</summary>
public sealed partial class DependencyServiceTests
{
    [Fact]
    public async Task Reinstall_over_an_untracked_file_from_a_corrupt_ledger_reports_the_unreadable_detail()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { d }, None, default); // places dxgi.dll; writes p.json

        _fs.AddFile("/game_mini/stellar/deps/p.json", new MockFileData("{ not valid json"));

        var st = Assert.Single(await s.EnsureAsync(G, "p", new[] { d }, None, default));

        Assert.Equal(DependencyState.Blocked, st.State);
        Assert.Equal("dxgi.dll; dependency record was unreadable; kept as p.json.corrupt", st.Detail);
        Assert.True(_fs.File.Exists("/game_mini/stellar/deps/p.json.corrupt"));
    }

    [Fact]
    public async Task Reinstall_after_a_corrupt_ledger_succeeds_and_notes_it_on_Installed()
    {
        _fs.AddFile("/game_mini/stellar/deps/p.json", new MockFileData("{ not valid json"));
        var d = File("fx", new byte[] { 1 }, "newfile.dll");

        var st = Assert.Single(await Make().EnsureAsync(G, "p", new[] { d }, None, default));

        Assert.Equal(DependencyState.Installed, st.State);
        Assert.Equal("dependency record was unreadable; kept as p.json.corrupt", st.Detail);
        Assert.True(_fs.File.Exists("/game_mini/newfile.dll"));
        Assert.True(_fs.File.Exists("/game_mini/stellar/deps/p.json.corrupt"));
    }
}

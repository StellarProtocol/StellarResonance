using System;
using System.Net.Http;
using System.Threading.Tasks;
using StellarLauncher.Core.Dependencies;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>Important (round 6): a write path (EnsureOneAsync, Remove, RemoveAll, DropFileFromLedger) must
/// never treat a PRESENT-but-currently-unreadable ledger (a transient IOException/UnauthorizedAccessException
/// while reading, as opposed to missing or corrupt-and-quarantined) as empty — that would silently overwrite
/// or delete whatever it actually holds. It must instead fail with "dependency record could not be read" and
/// touch nothing. Shares helpers/fields with <see cref="DependencyServiceTests"/>.</summary>
public sealed partial class DependencyServiceTests
{
    private const string LedgerPath = "/game_mini/stellar/deps/p.json";

    [Fact]
    public async Task A_present_but_transiently_unreadable_ledger_fails_the_second_dependency_without_touching_anything()
    {
        var a = File("a", new byte[] { 1 }, "a.dll");
        var b = File("b", new byte[] { 2 }, "b.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { a, b }, None, default); // both installed; p.json written with both entries
        var originalJson = _fs.File.ReadAllText(LedgerPath);

        // Let the FIRST ledger read (dependency "a") through; fail the SECOND (dependency "b").
        var faulty = new FaultInjectingFileSystem(_fs, LedgerPath, once: true, afterCalls: 1, "ReadAllText");
        var s2 = new DependencyService(faulty, new HttpClient(new Stub(this)));

        var a2 = File("a", new byte[] { 1 }, "a.dll"); // unchanged — stays Installed via the successful first read
        var b2 = File("b", new byte[] { 9 }, "newb.dll") with { Version = "2.0" }; // a different, currently absent destination

        var st = await s2.EnsureAsync(G, "p", new[] { a2, b2 }, None, default);

        Assert.Equal(DependencyState.Installed, st[0].State);
        Assert.Equal(DependencyState.Failed, st[1].State);
        Assert.Equal("dependency record could not be read", st[1].Detail);
        Assert.Equal(originalJson, _fs.File.ReadAllText(LedgerPath)); // byte-identical — untouched
        Assert.False(_fs.File.Exists("/game_mini/newb.dll")); // nothing written for "b"
    }

    [Fact]
    public async Task RemoveAll_does_not_touch_a_present_but_transiently_unreadable_ledger()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { d }, None, default);
        var originalJson = _fs.File.ReadAllText(LedgerPath);

        var faulty = new FaultInjectingFileSystem(_fs, LedgerPath, "ReadAllText");
        var s2 = new DependencyService(faulty, new HttpClient(new Stub(this)));

        var ex = Assert.Throws<InvalidOperationException>(() => s2.RemoveAll(G, "p"));

        Assert.Equal("dependency record could not be read", ex.Message);
        Assert.Equal(originalJson, _fs.File.ReadAllText(LedgerPath)); // untouched
        Assert.True(_fs.File.Exists("/game_mini/dxgi.dll")); // nothing deleted
    }
}

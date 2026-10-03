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

        // Let the undeclared-entry sweep's read (final review I3) and the read for dependency "a" through;
        // fail the next one (dependency "b").
        var faulty = new FaultInjectingFileSystem(_fs, LedgerPath, once: true, afterCalls: 2, "ReadAllText");
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

    // Controller round: a Skip's own Remove (I6) goes through the exact same TryReadForWrite guard — a
    // transient IOException on the ledger read must surface as Failed "dependency record could not be
    // read", not throw out of EnsureAsync, and must touch neither the files nor the ledger.
    [Fact]
    public async Task Skipped_with_a_transiently_unreadable_ledger_fails_without_touching_anything()
    {
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { d }, None, default); // installs; p.json written
        var originalJson = _fs.File.ReadAllText(LedgerPath);

        // The undeclared-entry sweep (final review I3) reads first; fail the skip's own read after it.
        var faulty = new FaultInjectingFileSystem(_fs, LedgerPath, once: true, afterCalls: 1, "ReadAllText");
        var s2 = new DependencyService(faulty, new HttpClient(new Stub(this)));

        var st = Assert.Single(await s2.EnsureAsync(G, "p", new[] { d }, new HashSet<string> { "fx" }, default));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.Equal("dependency record could not be read", st.Detail);
        Assert.Equal(originalJson, _fs.File.ReadAllText(LedgerPath)); // byte-identical — untouched
        Assert.True(_fs.File.Exists("/game_mini/dxgi.dll")); // the skip never got to delete anything
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

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => s2.RemoveAllAsync(G, "p"));

        Assert.Equal("dependency record could not be read", ex.Message);
        Assert.Equal(originalJson, _fs.File.ReadAllText(LedgerPath)); // untouched
        Assert.True(_fs.File.Exists("/game_mini/dxgi.dll")); // nothing deleted
    }
}

using System.Collections.Generic;
using StellarLauncher.Core.Dependencies;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>R1/R2 rule changes from the Task 4 review round: a Failed prerequisite leaves its dependent
/// untouched (not removed) and reports "waiting for &lt;id&gt;"; a forward requires reference fails
/// outright. Shares helpers/fields with <see cref="DependencyServiceTests"/>.</summary>
public sealed partial class DependencyServiceTests
{
    // R1: once a dependent is installed, a prerequisite that starts Failing on a later run (e.g. its file
    // was corrupted and the server now 404s) must NOT get the dependent removed — only reported as waiting.
    [Fact]
    public async Task Failed_prerequisite_leaves_an_already_installed_dependent_untouched()
    {
        var a = File("a", new byte[] { 1 }, "a.dll");
        var b = File("b", new byte[] { 2 }, "b.dll", requires: new[] { "a" });
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { a, b }, None, default); // both installed

        _fs.File.WriteAllBytes("/game_mini/a.dll", new byte[] { 99 }); // corrupt -> IsInstalled(a) now false
        _web.Remove("https://cdn/a");                                   // re-download now 404s -> Failed

        var st = await s.EnsureAsync(G, "p", new[] { a, b }, None, default);

        Assert.Equal(DependencyState.Failed, st[0].State);
        Assert.Equal(DependencyState.Failed, st[1].State);
        Assert.Equal("waiting for a", st[1].Detail);
        Assert.Equal(new byte[] { 2 }, _fs.File.ReadAllBytes("/game_mini/b.dll")); // untouched, not removed
    }

    // R2: a requires reference to a dependency listed LATER in the manifest is never silently satisfied.
    [Fact]
    public async Task Forward_requires_reference_fails_without_touching_disk()
    {
        var b = File("b", new byte[] { 2 }, "b.dll", requires: new[] { "a" });
        var a = File("a", new byte[] { 1 }, "a.dll"); // listed AFTER its dependent

        var st = await Make().EnsureAsync(G, "p", new[] { b, a }, None, default);

        Assert.Equal(DependencyState.Failed, st[0].State);
        Assert.Equal("requires a listed earlier", st[0].Detail);
        Assert.False(_fs.File.Exists("/game_mini/b.dll"));
        Assert.Equal(DependencyState.Installed, st[1].State); // "a" itself is unaffected
    }
}

using System.Threading.Tasks;
using StellarLauncher.Core.Dependencies;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>Status(...) coverage, including R1's Status-specific clause: a dependent whose prerequisite is
/// merely NotInstalled (never run, no network) shows NotInstalled too — not Skipped. Shares helpers/fields
/// with <see cref="DependencyServiceTests"/>.</summary>
public sealed partial class DependencyServiceTests
{
    [Fact]
    public void Status_reports_NotInstalled_for_a_plain_dependency_with_nothing_on_disk()
    {
        var d = File("a", new byte[] { 1 }, "a.dll");
        var st = Assert.Single(Make().Status(G, "p", new[] { d }, None));
        Assert.Equal(DependencyState.NotInstalled, st.State);
    }

    [Fact]
    public async Task Status_reports_Installed_after_EnsureAsync_without_any_network_call()
    {
        var d = File("a", new byte[] { 1 }, "a.dll");
        var s = Make();
        await s.EnsureAsync(G, "p", new[] { d }, None, default);
        _downloads = 0;

        var st = Assert.Single(s.Status(G, "p", new[] { d }, None));

        Assert.Equal(DependencyState.Installed, st.State);
        Assert.Equal(0, _downloads);
    }

    // R1: a prerequisite that is merely NotInstalled (nothing has run yet) must not make the dependent
    // read as Skipped — only an explicit skip or a Skipped/Blocked prerequisite does that.
    [Fact]
    public void Status_shows_NotInstalled_not_Skipped_when_prerequisite_is_merely_not_installed()
    {
        var a = File("a", new byte[] { 1 }, "a.dll");
        var b = File("b", new byte[] { 2 }, "b.dll", requires: new[] { "a" });

        var st = Make().Status(G, "p", new[] { a, b }, None);

        Assert.Equal(DependencyState.NotInstalled, st[0].State);
        Assert.Equal(DependencyState.NotInstalled, st[1].State);
    }

    // R1: an explicitly skipped prerequisite still propagates Skipped to its dependent in Status.
    [Fact]
    public void Status_shows_Skipped_for_a_dependent_of_an_explicitly_skipped_prerequisite()
    {
        var a = File("a", new byte[] { 1 }, "a.dll");
        var b = File("b", new byte[] { 2 }, "b.dll", requires: new[] { "a" });

        var st = Make().Status(G, "p", new[] { a, b }, new System.Collections.Generic.HashSet<string> { "a" });

        Assert.Equal(DependencyState.Skipped, st[0].State);
        Assert.Equal(DependencyState.Skipped, st[1].State);
    }
}

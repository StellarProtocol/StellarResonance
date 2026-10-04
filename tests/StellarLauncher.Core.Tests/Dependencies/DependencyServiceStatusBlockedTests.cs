using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>Task 6 follow-up: Status reports Blocked read-only — "my own dxgi.dll is in the way" shows on
/// the plugin page before any launch, with the same ownership rule EnsureAsync applies.</summary>
public sealed partial class DependencyServiceTests
{
    [Fact]
    public void Status_reports_Blocked_for_an_unowned_existing_file_with_its_path()
    {
        _fs.AddFile("/game_mini/dxgi.dll", new MockFileData(new byte[] { 9 }));
        var d = File("fx", new byte[] { 1 }, "dxgi.dll", modded: true);

        var st = Assert.Single(Make().Status(G, "p", new[] { d }, None));

        Assert.Equal(DependencyState.Blocked, st.State);
        Assert.Equal("dxgi.dll", st.Detail);
        Assert.Equal(new byte[] { 9 }, _fs.File.ReadAllBytes("/game_mini/dxgi.dll"));
        Assert.Equal(0, _downloads);
    }

    [Fact]
    public async Task Status_agrees_with_EnsureAsync_on_the_blocked_path()
    {
        _fs.AddFile("/game_mini/dxgi.dll", new MockFileData(new byte[] { 9 }));
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");
        var s = Make();

        var status = Assert.Single(s.Status(G, "p", new[] { d }, None));
        var ensured = Assert.Single(await s.EnsureAsync(G, "p", new[] { d }, None, default));

        Assert.Equal(ensured, status);
    }

    [Fact]
    public async Task Status_reports_Blocked_when_another_plugin_owns_the_destination()
    {
        var mine = File("fx", new byte[] { 1 }, "dxgi.dll");
        await Make().EnsureAsync(G, "other", new[] { mine }, None, default);

        var st = Assert.Single(Make().Status(G, "p", new[] { mine }, None));

        Assert.Equal(DependencyState.Blocked, st.State);
        Assert.Equal("dxgi.dll", st.Detail);
    }

    [Fact]
    public void Status_with_nothing_at_the_destination_stays_NotInstalled()
    {
        var st = Assert.Single(Make().Status(G, "p", new[] { File("fx", new byte[] { 1 }, "dxgi.dll") }, None));
        Assert.Equal(DependencyState.NotInstalled, st.State);
    }

    [Fact]
    public void Status_blocked_check_never_quarantines_a_corrupt_ledger()
    {
        _fs.AddFile("/game_mini/stellar/deps/other.json", new MockFileData("not json"));
        _fs.AddFile("/game_mini/dxgi.dll", new MockFileData(new byte[] { 9 }));

        Make().Status(G, "p", new[] { File("fx", new byte[] { 1 }, "dxgi.dll") }, None);

        Assert.True(_fs.File.Exists("/game_mini/stellar/deps/other.json"));
        Assert.False(_fs.File.Exists("/game_mini/stellar/deps/other.json.corrupt"));
    }

    [Fact]
    public void Status_checks_a_zips_exact_entry_destinations_but_not_its_directory_mappings()
    {
        _fs.AddFile("/game_mini/fx.dll", new MockFileData(new byte[] { 9 }));
        _fs.AddFile("/game_mini/shaders/a.fx", new MockFileData(new byte[] { 9 }));
        PluginDependency Zip(params PluginDependencyFile[] files) =>
            new("z", "z", "1.0", "https://cdn/z", new string('a', 64), 10, "zip", files, "game", License: "MIT", LicenseUrl: "https://l", SourceUrl: "https://s");

        var exact = Assert.Single(Make().Status(G, "p", new[] { Zip(new PluginDependencyFile("bin/fx.dll", "fx.dll")) }, None));
        var dir = Assert.Single(Make().Status(G, "p", new[] { Zip(new PluginDependencyFile("shaders/", "shaders/")) }, None));

        Assert.Equal(DependencyState.Blocked, exact.State);
        Assert.Equal("fx.dll", exact.Detail);
        Assert.Equal(DependencyState.NotInstalled, dir.State); // contents unknown until downloaded
    }

    [Fact]
    public void Status_skips_a_dependent_of_a_blocked_prerequisite()
    {
        _fs.AddFile("/game_mini/dxgi.dll", new MockFileData(new byte[] { 9 }));
        var a = File("a", new byte[] { 1 }, "dxgi.dll");
        var b = File("b", new byte[] { 2 }, "b.dll", requires: new[] { "a" });

        var st = Make().Status(G, "p", new[] { a, b }, None);

        Assert.Equal(DependencyState.Blocked, st[0].State);
        Assert.Equal(DependencyState.Skipped, st[1].State);
    }
}

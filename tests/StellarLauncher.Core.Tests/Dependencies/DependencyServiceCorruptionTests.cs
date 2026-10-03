using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading.Tasks;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>Important 2(c): when a plugin's own ledger is found unreadable, the note "dependency record
/// was unreadable; kept as &lt;id&gt;.json.corrupt" is attached ONLY to a Blocked or Failed status for
/// that plugin — that's where it explains something. Installed/Skipped/NotInstalled never carry it, since
/// the <c>.corrupt</c> evidence is never auto-deleted and would otherwise leave a scary note on a healthy
/// install forever. Shares helpers/fields with <see cref="DependencyServiceTests"/>.</summary>
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

    // Follow-up: Installed is a FINE outcome — it must never carry the note, even though the ledger was
    // found corrupt (and quarantined) during this very call.
    [Fact]
    public async Task Reinstall_after_a_corrupt_ledger_succeeds_with_no_note_on_Installed()
    {
        _fs.AddFile("/game_mini/stellar/deps/p.json", new MockFileData("{ not valid json"));
        var d = File("fx", new byte[] { 1 }, "newfile.dll");

        var st = Assert.Single(await Make().EnsureAsync(G, "p", new[] { d }, None, default));

        Assert.Equal(DependencyState.Installed, st.State);
        Assert.Null(st.Detail); // no note on a fine outcome
        Assert.True(_fs.File.Exists("/game_mini/newfile.dll"));
        Assert.True(_fs.File.Exists("/game_mini/stellar/deps/p.json.corrupt")); // evidence still kept
    }

    // Important 2 (round 4): the note is derived from whether <id>.json.corrupt exists on disk, not from
    // whether THIS call quarantined it — so a caller that reads AFTER the corruption was already
    // discovered (e.g. Park's ReadAll ran first) still gets it on a Blocked outcome.
    [Fact]
    public async Task Park_discovering_corruption_first_still_lets_EnsureAsync_report_the_note_on_Blocked()
    {
        _fs.AddFile("/game_mini/stellar/deps/p.json", new MockFileData("{ not valid json"));
        _fs.AddFile("/game_mini/dxgi.dll", new MockFileData(new byte[] { 9 })); // untracked once "p"'s ledger is gone
        var s = Make();

        await s.ParkModdedOnlyAsync(G); // discovers + quarantines "p"'s ledger as a side effect of ReadAll
        Assert.True(_fs.File.Exists("/game_mini/stellar/deps/p.json.corrupt"));

        var d = File("fx", new byte[] { 1 }, "dxgi.dll");
        var st = Assert.Single(await s.EnsureAsync(G, "p", new[] { d }, None, default));

        Assert.Equal(DependencyState.Blocked, st.State);
        Assert.Equal("dxgi.dll; dependency record was unreadable; kept as p.json.corrupt", st.Detail);
    }

    // Important 2 (round 4): the note is not one-shot — it keeps appearing on a LATER Blocked run too,
    // since the .corrupt evidence is never cleaned up automatically.
    [Fact]
    public async Task The_unreadable_note_keeps_appearing_on_a_later_Blocked_run_too()
    {
        _fs.AddFile("/game_mini/stellar/deps/p.json", new MockFileData("{ not valid json"));
        _fs.AddFile("/game_mini/dxgi.dll", new MockFileData(new byte[] { 9 })); // untracked once "p"'s ledger is gone
        var s = Make();
        var d1 = File("fx", new byte[] { 1 }, "newfile.dll"); // first attempt: a clean install, discovers + quarantines
        await s.EnsureAsync(G, "p", new[] { d1 }, None, default);

        var d2 = File("fx2", new byte[] { 2 }, "dxgi.dll"); // a LATER attempt that collides with the untracked file
        var st = Assert.Single(await s.EnsureAsync(G, "p", new[] { d2 }, None, default));

        Assert.Equal(DependencyState.Blocked, st.State);
        Assert.Equal("dxgi.dll; dependency record was unreadable; kept as p.json.corrupt", st.Detail);
    }

    // Follow-up: once the player resolves the conflict themselves (removes the untracked file), the
    // dependency installs cleanly and the Installed status carries no note — even though the .corrupt
    // evidence from the earlier corruption is still sitting on disk, untouched.
    [Fact]
    public async Task After_the_conflict_is_resolved_the_Installed_status_carries_no_note()
    {
        _fs.AddFile("/game_mini/stellar/deps/p.json", new MockFileData("{ not valid json"));
        _fs.AddFile("/game_mini/dxgi.dll", new MockFileData(new byte[] { 9 })); // the conflicting untracked file
        var s = Make();
        var d = File("fx", new byte[] { 1 }, "dxgi.dll");

        var blocked = Assert.Single(await s.EnsureAsync(G, "p", new[] { d }, None, default));
        Assert.Equal(DependencyState.Blocked, blocked.State); // confirms the conflict exists first

        _fs.File.Delete("/game_mini/dxgi.dll"); // the player removes the conflicting file

        var st = Assert.Single(await s.EnsureAsync(G, "p", new[] { d }, None, default));

        Assert.Equal(DependencyState.Installed, st.State);
        Assert.Null(st.Detail); // no note, even though the .corrupt evidence is still sitting on disk
        Assert.True(_fs.File.Exists("/game_mini/stellar/deps/p.json.corrupt")); // evidence never deleted
    }

    // Controller round: BuildFiles now runs inside the download/verify try (DependencyService.Placement.cs),
    // so an archive-shape failure (here, "no files matched") is returned directly — it must never pick up
    // the unreadable-record note even though this plugin's OWN ledger is corrupt (and gets quarantined)
    // during this very call. Before the fix, BuildFiles's exception escaped to EnsureAsync's catch-all,
    // which treated it as ledger-handling fallout and appended the note.
    [Fact]
    public async Task An_archive_shape_failure_never_carries_the_unreadable_note_even_with_a_corrupt_ledger()
    {
        _fs.AddFile("/game_mini/stellar/deps/p.json", new MockFileData("{ not valid json"));

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        using (var w = new StreamWriter(zip.CreateEntry("other/thing.txt").Open())) w.Write("x");
        var bytes = ms.ToArray();
        _web["https://cdn/nomatch"] = bytes;
        var d = new PluginDependency("nomatch", "nomatch", "1", "https://cdn/nomatch", Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, "zip",
            new[] { new PluginDependencyFile("pack/", "out/") }, "plugin");

        var st = Assert.Single(await Make().EnsureAsync(G, "p", new[] { d }, None, default));

        Assert.Equal(DependencyState.Failed, st.State);
        Assert.Equal("no files matched", st.Detail); // never annotated, even though p.json is corrupt right now
        Assert.True(_fs.File.Exists("/game_mini/stellar/deps/p.json.corrupt"));
    }
}

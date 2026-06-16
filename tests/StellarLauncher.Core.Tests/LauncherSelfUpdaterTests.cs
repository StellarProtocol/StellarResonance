using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading.Tasks;
using StellarLauncher.Core.Services;
using Xunit;

public class LauncherSelfUpdaterTests
{
    private static (byte[] zip, string sha) MakeZip()
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var w = new StreamWriter(z.CreateEntry("StellarLauncher.App").Open());
            w.Write("new-binary");
        }
        var bytes = ms.ToArray();
        return (bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    [Fact]
    public async Task Stage_verifies_and_extracts()
    {
        var (zip, sha) = MakeZip();
        var fs = new MockFileSystem();
        var updater = new LauncherSelfUpdater(fs);

        await updater.StageAsync(new MemoryStream(zip), sha, "/staging");

        Assert.True(fs.File.Exists("/staging/StellarLauncher.App"));
    }

    [Fact]
    public async Task Stage_rejects_sha_mismatch()
    {
        var (zip, _) = MakeZip();
        var fs = new MockFileSystem();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new LauncherSelfUpdater(fs).StageAsync(new MemoryStream(zip), "deadbeef", "/staging"));
        Assert.False(fs.Directory.Exists("/staging/StellarLauncher.App"));
    }

    [Fact]
    public void Unix_swap_script_waits_for_exit_then_copies_and_relaunches()
    {
        var s = new LauncherSelfUpdater(new MockFileSystem())
            .BuildUnixSwapScript("/staging", "/app", "StellarLauncher.App", 4242);
        // Must wait for the OLD process to exit before touching files (avoids ETXTBSY / SIGBUS on mmap'd libs).
        Assert.Contains("kill -0 4242", s);
        Assert.Contains("cp -a \"/staging/.\" \"/app/\"", s);
        Assert.Contains("chmod +x \"/app/StellarLauncher.App\"", s);
        Assert.Contains("\"/app/StellarLauncher.App\" &", s);  // relaunch
        Assert.Contains("rm -f", s);                            // self-delete
    }

    [Fact]
    public void CleanupStaleUpdate_removes_leftover_old_and_new()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/app/StellarLauncher.App", new MockFileData("current"));
        fs.AddFile("/app/StellarLauncher.App.old", new MockFileData("stale"));
        fs.AddFile("/app/StellarLauncher.App.new", new MockFileData("aborted"));

        new LauncherSelfUpdater(fs).CleanupStaleUpdate("/app", "StellarLauncher.App");

        Assert.True(fs.File.Exists("/app/StellarLauncher.App"));
        Assert.False(fs.File.Exists("/app/StellarLauncher.App.old"));
        Assert.False(fs.File.Exists("/app/StellarLauncher.App.new"));
    }

    [Fact]
    public void Windows_swap_script_copies_and_relaunches()
    {
        var s = new LauncherSelfUpdater(new MockFileSystem())
            .BuildWindowsSwapScript(@"C:\staging", @"C:\app", "StellarLauncher.App.exe");
        Assert.Contains("robocopy", s);
        Assert.Contains(@"C:\staging", s);
        Assert.Contains(@"C:\app", s);
        Assert.Contains("StellarLauncher.App.exe", s);
        Assert.Contains("start", s);
    }
}

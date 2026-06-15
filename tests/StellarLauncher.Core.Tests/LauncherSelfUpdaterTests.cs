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

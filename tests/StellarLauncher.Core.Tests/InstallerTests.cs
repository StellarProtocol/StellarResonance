using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using StellarLauncher.Core.Services;
using Xunit;

public class InstallerTests
{
    private static (byte[] zip, string sha) MakeZip()
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = z.CreateEntry("BepInEx/plugins/Stellar.Framework/Stellar.Host.dll");
            using var w = new StreamWriter(e.Open());
            w.Write("dll-bytes");
        }
        var bytes = ms.ToArray();
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return (bytes, sha);
    }

    [Fact]
    public async Task Verifies_extracts_and_records_version()
    {
        var (zip, sha) = MakeZip();
        var fs = new MockFileSystem();
        fs.AddDirectory("/game_mini");
        var installer = new Installer(fs);

        await installer.InstallAsync(new MemoryStream(zip), sha, "/game_mini", "1.0.0");

        Assert.True(fs.File.Exists("/game_mini/BepInEx/plugins/Stellar.Framework/Stellar.Host.dll"));
        Assert.Equal("1.0.0", installer.ReadInstalledVersion("/game_mini"));
    }

    [Fact]
    public async Task Rejects_sha_mismatch_without_writing()
    {
        var (zip, _) = MakeZip();
        var fs = new MockFileSystem();
        fs.AddDirectory("/game_mini");
        var installer = new Installer(fs);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.InstallAsync(new MemoryStream(zip), "deadbeef", "/game_mini", "1.0.0"));

        Assert.False(fs.Directory.Exists("/game_mini/BepInEx"));
    }

    [Fact]
    public async Task Rejects_zip_slip_entry()
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = z.CreateEntry("../../escape.txt");
            using var w = new StreamWriter(e.Open());
            w.Write("pwned");
        }
        var bytes = ms.ToArray();
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var fs = new MockFileSystem();
        fs.AddDirectory("/game/release_2.11/game_mini");
        var installer = new Installer(fs);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.InstallAsync(new MemoryStream(bytes), sha,
                                   "/game/release_2.11/game_mini", "1.0.0"));
        Assert.False(fs.File.Exists("/game/escape.txt"));
    }
}

using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading.Tasks;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;
using Xunit;

public class EndToEndTests
{
    private sealed class Win : IPlatformInfo { public bool IsWindows => true; public string AppDataDir => "/cfg"; }

    [Fact]
    public async Task Locate_install_toggle_launch_chain()
    {
        // real temp dir so ZipArchive + System.IO line up with a real FileSystem
        var tmp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var gameRoot = Path.Combine(tmp, "StarLauncher", "game");
            var gameMini = Path.Combine(gameRoot, "release_2.11", "game_mini");
            Directory.CreateDirectory(gameMini);
            File.WriteAllText(Path.Combine(gameMini, "doorstop_config.ini"),
                "[General]\nenabled = true\n");

            var fs = new FileSystem();
            Assert.Equal(gameMini, new GameLocator(fs).FindGameMini(gameRoot));

            // build a bundle zip
            var bundlePath = Path.Combine(tmp, "b.zip");
            using (var z = ZipFile.Open(bundlePath, ZipArchiveMode.Create))
                z.CreateEntry("BepInEx/plugins/Stellar.Framework/x.dll");
            var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bundlePath))).ToLowerInvariant();

            var installer = new Installer(fs);
            await using (var s = File.OpenRead(bundlePath))
                await installer.InstallAsync(s, sha, gameMini, "1.0.0");
            Assert.Equal("1.0.0", installer.ReadInstalledVersion(gameMini));

            var doorstop = new DoorstopToggle(fs);
            doorstop.SetEnabled(Path.Combine(gameMini, "doorstop_config.ini"), false);
            Assert.False(doorstop.IsEnabled(Path.Combine(gameMini, "doorstop_config.ini")));

            var psi = new GameLauncher(new Win())
                .BuildStartInfo(new LaunchRequest(Path.Combine(tmp, "StarLauncher", "StarLauncher.exe")));
            Assert.EndsWith("StarLauncher.exe", psi.FileName);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }
}

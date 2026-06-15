using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using StellarLauncher.Core.Services;
using Xunit;

public class PluginInstallerTests
{
    private static (byte[] bytes, string sha) Dll(string content = "plugin-bytes")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return (bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    [Fact]
    public async Task Installs_verifies_and_reports_version()
    {
        var (bytes, sha) = Dll();
        var fs = new MockFileSystem();
        fs.AddDirectory("/game_mini");
        var installer = new PluginInstaller(fs);

        await installer.InstallAsync(new MemoryStream(bytes), sha, "/game_mini",
            pluginId: "combatmeter", dllFileName: "Stellar.CombatMeter.dll", version: "1.0.0");

        Assert.True(fs.File.Exists("/game_mini/stellar/plugins/combatmeter/Stellar.CombatMeter.dll"));
        Assert.Equal("1.0.0", installer.InstalledVersion("/game_mini", "combatmeter"));
        Assert.True(installer.IsInstalled("/game_mini", "combatmeter"));
    }

    [Fact]
    public async Task Rejects_sha_mismatch_without_writing()
    {
        var (bytes, _) = Dll();
        var fs = new MockFileSystem();
        fs.AddDirectory("/game_mini");
        var installer = new PluginInstaller(fs);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            installer.InstallAsync(new MemoryStream(bytes), "deadbeef", "/game_mini",
                "combatmeter", "Stellar.CombatMeter.dll", "1.0.0"));
        Assert.False(fs.Directory.Exists("/game_mini/stellar/plugins/combatmeter"));
    }

    [Theory]
    [InlineData("../evil", "x.dll")]
    [InlineData("ok", "../../evil.dll")]
    [InlineData("a/b", "x.dll")]
    public async Task Rejects_path_traversal_in_id_or_filename(string id, string file)
    {
        var (bytes, sha) = Dll();
        var fs = new MockFileSystem();
        fs.AddDirectory("/game_mini");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new PluginInstaller(fs).InstallAsync(new MemoryStream(bytes), sha, "/game_mini", id, file, "1.0.0"));
    }

    [Fact]
    public async Task Remove_deletes_the_plugin_folder()
    {
        var (bytes, sha) = Dll();
        var fs = new MockFileSystem();
        fs.AddDirectory("/game_mini");
        var installer = new PluginInstaller(fs);
        await installer.InstallAsync(new MemoryStream(bytes), sha, "/game_mini",
            "combatmeter", "Stellar.CombatMeter.dll", "1.0.0");

        installer.Remove("/game_mini", "combatmeter");

        Assert.False(installer.IsInstalled("/game_mini", "combatmeter"));
        Assert.Null(installer.InstalledVersion("/game_mini", "combatmeter"));
    }
}

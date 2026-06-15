using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.Core.Services;
using Xunit;

public class DoorstopToggleTests
{
    private const string Ini =
        "[General]\n" +
        "enabled = true\n" +
        "target_assembly = BepInEx\\core\\BepInEx.Preloader.dll\n" +
        "\n" +
        "[Debug]\n" +
        "enabled = false\n";   // must NOT be touched

    private static (MockFileSystem, string) Setup(string body)
    {
        var fs = new MockFileSystem();
        var path = "/game/doorstop_config.ini";
        fs.AddFile(path, new MockFileData(body));
        return (fs, path);
    }

    [Fact]
    public void Reads_enabled_state()
    {
        var (fs, path) = Setup(Ini);
        Assert.True(new DoorstopToggle(fs).IsEnabled(path));
    }

    [Fact]
    public void Disables_only_general_section()
    {
        var (fs, path) = Setup(Ini);
        var t = new DoorstopToggle(fs);
        t.SetEnabled(path, false);

        var text = fs.File.ReadAllText(path);
        Assert.Contains("[General]\nenabled = false", text);
        Assert.Contains("[Debug]\nenabled = false", text);  // unchanged
        Assert.False(t.IsEnabled(path));
    }
}

using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.Core.Services;
using Xunit;

public class BepInExConfigTests
{
    private const string Cfg = """
        [Logging]
        UnityLogListening = true

        [Logging.Console]
        Enabled = true

        [Logging.Disk]
        Enabled = true
        InstantFlushing = true
        """;

    private static MockFileSystem FsWith(string cfg)
    {
        var fs = new MockFileSystem();
        fs.AddFile("/gm/BepInEx/config/BepInEx.cfg", new MockFileData(cfg));
        return fs;
    }

    [Fact]
    public void Prod_disables_console_flush_and_unitylog_but_keeps_disk_enabled()
    {
        var fs = FsWith(Cfg);
        new BepInExConfig(fs).ApplyMode("/gm", debug: false);
        var lines = fs.File.ReadAllLines("/gm/BepInEx/config/BepInEx.cfg");

        Assert.Contains("UnityLogListening = false", lines);
        Assert.Contains("InstantFlushing = false", lines);
        // The Console section's Enabled flips to false; the Disk section's Enabled stays true.
        Assert.Equal("false", ValueOf(lines, "[Logging.Console]", "Enabled"));
        Assert.Equal("true", ValueOf(lines, "[Logging.Disk]", "Enabled"));
    }

    [Fact]
    public void Debug_enables_console_and_flush_but_unitylog_stays_off()
    {
        var fs = FsWith(Cfg.Replace("= true", "= false"));   // start from prod-ish
        new BepInExConfig(fs).ApplyMode("/gm", debug: true);
        var lines = fs.File.ReadAllLines("/gm/BepInEx/config/BepInEx.cfg");

        Assert.Equal("true", ValueOf(lines, "[Logging.Console]", "Enabled"));
        Assert.Contains("InstantFlushing = true", lines);
        Assert.Contains("UnityLogListening = false", lines);   // always off (perf)
    }

    [Fact]
    public void Missing_cfg_is_a_no_op()
    {
        var fs = new MockFileSystem();
        new BepInExConfig(fs).ApplyMode("/gm", debug: false);   // must not throw
    }

    private static string ValueOf(string[] lines, string section, string key)
    {
        string? cur = null;
        foreach (var l in lines)
        {
            var t = l.Trim();
            if (t.StartsWith('[')) cur = t;
            else if (cur == section && t.StartsWith(key)) return t.Split('=')[1].Trim();
        }
        return "(not found)";
    }
}

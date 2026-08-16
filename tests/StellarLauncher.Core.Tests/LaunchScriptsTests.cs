using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using StellarLauncher.Core.Services;

public class LaunchScriptsTests
{
    private static readonly KeyValuePair<string, string?>[] NoEnv = Array.Empty<KeyValuePair<string, string?>>();

    [Fact]
    public async Task Returns_the_scripts_exit_code()
    {
        if (!OperatingSystem.IsLinux()) return;   // /bin/sh — Linux only
        var f = Path.GetTempFileName();
        File.WriteAllText(f, "exit 3\n");
        var code = await LaunchScripts.RunAsync(f, NoEnv, TimeSpan.FromSeconds(5));
        Assert.Equal(3, code);
    }

    [Fact]
    public async Task Returns_null_on_timeout()
    {
        if (!OperatingSystem.IsLinux()) return;
        var f = Path.GetTempFileName();
        File.WriteAllText(f, "sleep 5\n");
        var code = await LaunchScripts.RunAsync(f, NoEnv, TimeSpan.FromMilliseconds(200));
        Assert.Null(code);
    }

    [Fact]
    public async Task Passes_the_supplied_environment()
    {
        if (!OperatingSystem.IsLinux()) return;
        var outFile = Path.GetTempFileName();
        var script = Path.GetTempFileName();
        File.WriteAllText(script, "echo hi > \"$OUT\"\n");
        var env = new[] { new KeyValuePair<string, string?>("OUT", outFile) };
        await LaunchScripts.RunAsync(script, env, TimeSpan.FromSeconds(5));
        Assert.Equal("hi", File.ReadAllText(outFile).Trim());
    }

    [Fact]
    public async Task Kills_the_process_on_timeout()
    {
        if (!OperatingSystem.IsLinux()) return;   // /bin/sh — Linux only
        var outFile = Path.GetTempFileName();
        var script = Path.GetTempFileName();
        // Writes "a" immediately, then (if allowed to keep running) "b" after 3s.
        File.WriteAllText(script, "echo a > \"$OUT\"\nsleep 3\necho b >> \"$OUT\"\n");
        var env = new[] { new KeyValuePair<string, string?>("OUT", outFile) };

        var code = await LaunchScripts.RunAsync(script, env, TimeSpan.FromMilliseconds(200));
        Assert.Null(code);                       // timed out

        await Task.Delay(4000);                   // wait past the script's 3s sleep
        var contents = File.ReadAllText(outFile);
        Assert.Contains("a", contents);           // it started
        Assert.DoesNotContain("b", contents);     // but was killed before the post-sleep write
    }
}

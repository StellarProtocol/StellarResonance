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

    [Fact]
    public async Task Start_runs_a_parallel_script_and_the_handle_kills_it()
    {
        if (!OperatingSystem.IsLinux()) return;   // /bin/sh — Linux only
        var outFile = Path.GetTempFileName();
        var script = Path.GetTempFileName();
        // Marks that it ran, then (if not killed) writes "late" after 4s.
        File.WriteAllText(script, "echo up > \"$OUT\"\nsleep 4\necho late >> \"$OUT\"\n");
        var env = new[] { new KeyValuePair<string, string?>("OUT", outFile) };

        using var handle = LaunchScripts.Start(script, env);
        Assert.NotNull(handle);
        for (var i = 0; i < 50 && File.ReadAllText(outFile).Length == 0; i++) await Task.Delay(50);
        Assert.Contains("up", File.ReadAllText(outFile));   // running alongside
        Assert.False(handle!.HasExited);

        handle.Kill();                                       // "closed when the game closed"
        for (var i = 0; i < 50 && !handle.HasExited; i++) await Task.Delay(50);
        Assert.True(handle.HasExited);

        await Task.Delay(4500);                              // past the script's own 4s sleep
        Assert.DoesNotContain("late", File.ReadAllText(outFile));   // killed before the post-sleep write
    }
}

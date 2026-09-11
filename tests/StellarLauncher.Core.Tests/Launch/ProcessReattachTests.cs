using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Launch;
using Xunit;

public class ProcessReattachTests
{
    private const string Main = "/opt/game/BlueProtocol/drive_c/Star/StarLauncher/game/release_3.7/game_mini";
    private const string Test = "/opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_3.7/game_mini";

    private sealed class Fake(params RunningProcess[] procs) : IRunningProcessScanner
    {
        public IReadOnlyList<RunningProcess> Snapshot() => procs;
    }

    [Fact]
    public void ClientRoot_is_the_StarLauncher_dir_for_official_layouts_else_game_mini()
    {
        Assert.Equal("/opt/game/BlueProtocol/drive_c/Star/StarLauncher", ProcessReattach.ClientRoot(Main));
        Assert.Equal(@"D:\S\steamapps\common\BP".Replace('\\', '/'), ProcessReattach.ClientRoot(@"D:\S\steamapps\common\BP\"));
    }

    [Fact]
    public void Matches_wine_and_native_command_lines_for_the_right_client_only()
    {
        var wine = @"Z:\opt\game\BlueProtocol\drive_c\Star\StarLauncher\StarLauncher.exe";
        var unity = "/opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_3.7/game_mini/StarSEA.exe -screen-fullscreen 1";
        Assert.True(ProcessReattach.Matches(wine, Main));
        Assert.False(ProcessReattach.Matches(wine, Test));     // BlueProtocol vs BlueProtocol2 — no prefix match
        Assert.True(ProcessReattach.Matches(unity, Test));
        Assert.False(ProcessReattach.Matches("/usr/bin/heroic", Main));
    }

    [Fact]
    public void Find_returns_the_first_matching_pid()
    {
        var scanner = new Fake(
            new RunningProcess(100, "/usr/lib/systemd/systemd"),
            new RunningProcess(4242, @"C:\windows\system32\start.exe /exec Z:\opt\game\BlueProtocol2\drive_c\Star\StarLauncher\StarLauncher.exe"),
            new RunningProcess(4300, "/opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_3.7/game_mini/StarSEA.exe"));
        Assert.Equal(4242, ProcessReattach.Find(new ClientProfile { GameMiniDir = Test }, scanner));
        Assert.Null(ProcessReattach.Find(new ClientProfile { GameMiniDir = Main }, scanner));
    }

    [Fact]
    public void ProcFsScanner_reads_nul_separated_cmdlines_and_skips_junk()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/proc/4242/cmdline", new MockFileData(new byte[] { (byte)'a', 0, (byte)'b', 0 }));
        fs.AddFile("/proc/self/cmdline", new MockFileData("x"));         // not numeric → skipped
        fs.AddDirectory("/proc/77");                                      // no cmdline → skipped
        var snap = new ProcFsScanner(fs).Snapshot();
        var p = Assert.Single(snap);
        Assert.Equal(4242, p.Pid);
        Assert.Equal("a b", p.CommandLine);
    }
}

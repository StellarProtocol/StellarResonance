using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;
using Xunit;

public class GameLauncherTests
{
    private sealed class FakePlatform : IPlatformInfo
    {
        public bool IsWindows { get; init; }
        public string AppDataDir => "/cfg";
    }

    private static GameLauncher Linux() => new(new FakePlatform { IsWindows = false });

    [Fact]
    public void Windows_starts_starlauncher_exe()
    {
        var l = new GameLauncher(new FakePlatform { IsWindows = true });
        var psi = l.BuildStartInfo(new LaunchRequest(
            StarLauncherExe: @"C:\Star\StarLauncher\StarLauncher.exe"));
        Assert.Equal(@"C:\Star\StarLauncher\StarLauncher.exe", psi.FileName);
        Assert.True(psi.UseShellExecute);
    }

    [Fact]
    public void Linux_wine_runs_exe_with_wineprefix()
    {
        var psi = Linux().BuildStartInfo(new LaunchRequest(
            StarLauncherExe: "/prefix/drive_c/Star/StarLauncher/StarLauncher.exe",
            Runner: "/usr/bin/wine",
            WinePrefix: "/home/u/.prefix"));
        Assert.Equal("/usr/bin/wine", psi.FileName);
        Assert.Contains("StarLauncher.exe", psi.ArgumentList[^1]);
        Assert.Equal("winhttp=n,b", psi.Environment["WINEDLLOVERRIDES"]);
        Assert.Equal("/home/u/.prefix", psi.Environment["WINEPREFIX"]);
    }

    [Fact]
    public void Linux_proton_uses_umu_with_protonpath()
    {
        var psi = Linux().BuildStartInfo(new LaunchRequest(
            StarLauncherExe: "/prefix/drive_c/Star/StarLauncher/StarLauncher.exe",
            Runner: "/home/u/.config/heroic/tools/proton/GE-Proton10-26/proton",
            WinePrefix: "/opt/game/BP2",
            UmuRun: "/usr/bin/umu-run"));
        Assert.Equal("/usr/bin/umu-run", psi.FileName);
        Assert.Contains("StarLauncher.exe", psi.ArgumentList[^1]);
        Assert.Equal("/opt/game/BP2", psi.Environment["WINEPREFIX"]);
        Assert.Equal("/home/u/.config/heroic/tools/proton/GE-Proton10-26", psi.Environment["PROTONPATH"]);
        Assert.Equal("winhttp=n,b", psi.Environment["WINEDLLOVERRIDES"]);
    }

    [Fact]
    public void Linux_proton_without_umu_falls_back_to_steam_compat()
    {
        var psi = Linux().BuildStartInfo(new LaunchRequest(
            StarLauncherExe: "/p/drive_c/Star/StarLauncher/StarLauncher.exe",
            Runner: "/opt/proton/proton",
            WinePrefix: "/opt/game/BP2"));
        Assert.Equal("/opt/proton/proton", psi.FileName);
        Assert.Equal("run", psi.ArgumentList[0]);
        Assert.Equal("/opt/game/BP2", psi.Environment["STEAM_COMPAT_DATA_PATH"]);
    }
}

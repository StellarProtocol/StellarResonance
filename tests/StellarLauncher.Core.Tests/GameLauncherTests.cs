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
    public void Linux_applies_sync_overlay_and_nvapi_tweaks()
    {
        var psi = Linux().BuildStartInfo(new LaunchRequest(
            StarLauncherExe: "/p/drive_c/Star/StarLauncher/StarLauncher.exe",
            Runner: "/usr/bin/wine", WinePrefix: "/p",
            Esync: true, Fsync: true, Overlay: PerfOverlayMode.Fps, DxvkNvapi: true, StellarPerf: true));
        Assert.Equal("1", psi.Environment["WINEESYNC"]);
        Assert.Equal("1", psi.Environment["WINEFSYNC"]);
        Assert.Equal("fps", psi.Environment["DXVK_HUD"]);        // FPS counter → DXVK counter
        Assert.Equal("1", psi.Environment["STELLAR_PERFHUD"]);   // Stellar Perf → framework overlay
        Assert.Contains("nvapi,nvapi64=n,b", psi.Environment["WINEDLLOVERRIDES"]);
        Assert.Contains("winhttp=n,b", psi.Environment["WINEDLLOVERRIDES"]);
    }

    [Fact]
    public void Linux_fps_counter_and_perf_overlay_are_independent()
    {
        var fpsOnly = Linux().BuildStartInfo(new LaunchRequest(
            StarLauncherExe: "/p/x.exe", Runner: "/usr/bin/wine", WinePrefix: "/p", Overlay: PerfOverlayMode.Fps));
        Assert.Equal("fps", fpsOnly.Environment["DXVK_HUD"]);
        Assert.False(fpsOnly.Environment.ContainsKey("STELLAR_PERFHUD"));

        var perfOnly = Linux().BuildStartInfo(new LaunchRequest(
            StarLauncherExe: "/p/x.exe", Runner: "/usr/bin/wine", WinePrefix: "/p", StellarPerf: true));
        Assert.Equal("1", perfOnly.Environment["STELLAR_PERFHUD"]);
        Assert.False(perfOnly.Environment.ContainsKey("DXVK_HUD"));
    }

    [Fact]
    public void Linux_full_overlay_activates_mangohud_with_preset()
    {
        var psi = Linux().BuildStartInfo(new LaunchRequest(
            StarLauncherExe: "/p/x.exe", Runner: "/usr/bin/wine", WinePrefix: "/p",
            Overlay: PerfOverlayMode.Full, MangoHudPreset: MangoHud.DefaultPreset));
        Assert.Equal("1", psi.Environment["MANGOHUD"]);
        Assert.Equal(MangoHud.DefaultPreset, psi.Environment["MANGOHUD_CONFIG"]);
        Assert.False(psi.Environment.ContainsKey("DXVK_HUD"));   // modes are exclusive
    }

    [Fact]
    public void Linux_full_overlay_respects_user_mangohud_config()
    {
        // Null preset = the user has their own MangoHud.conf; MANGOHUD_CONFIG must stay unset
        // (the env var would override their file).
        var psi = Linux().BuildStartInfo(new LaunchRequest(
            StarLauncherExe: "/p/x.exe", Runner: "/usr/bin/wine", WinePrefix: "/p",
            Overlay: PerfOverlayMode.Full, MangoHudPreset: null));
        Assert.Equal("1", psi.Environment["MANGOHUD"]);
        Assert.False(psi.Environment.ContainsKey("MANGOHUD_CONFIG"));
    }

    [Fact]
    public void Linux_overlay_off_sets_no_hud_env()
    {
        var psi = Linux().BuildStartInfo(new LaunchRequest(
            StarLauncherExe: "/p/x.exe", Runner: "/usr/bin/wine", WinePrefix: "/p",
            Overlay: PerfOverlayMode.Off));
        Assert.False(psi.Environment.ContainsKey("DXVK_HUD"));
        Assert.False(psi.Environment.ContainsKey("MANGOHUD"));
        Assert.False(psi.Environment.ContainsKey("MANGOHUD_CONFIG"));
    }

    [Fact]
    public void Settings_overlay_tristate_migrates_legacy_fps_bool()
    {
        // Pre-1.2.26 settings: only the legacy bool exists ("" = tri-state unset).
        Assert.Equal(PerfOverlayMode.Fps, new LauncherSettings { FpsOverlay = true }.EffectiveOverlay());
        Assert.Equal(PerfOverlayMode.Off, new LauncherSettings { FpsOverlay = false }.EffectiveOverlay());

        // New tri-state key wins over the legacy bool.
        Assert.Equal(PerfOverlayMode.Full, new LauncherSettings { FpsOverlay = false, PerfOverlay = "full" }.EffectiveOverlay());
        Assert.Equal(PerfOverlayMode.Off, new LauncherSettings { FpsOverlay = true, PerfOverlay = "off" }.EffectiveOverlay());

        // SetOverlay mirrors the legacy bool for downgrade compatibility.
        var s = new LauncherSettings();
        s.SetOverlay(PerfOverlayMode.Full);
        Assert.Equal("full", s.PerfOverlay);
        Assert.True(s.FpsOverlay);
        s.SetOverlay(PerfOverlayMode.Off);
        Assert.Equal("off", s.PerfOverlay);
        Assert.False(s.FpsOverlay);
    }

    [Fact]
    public void Linux_disabling_sync_sets_wine_off_and_proton_no_flags()
    {
        var wine = Linux().BuildStartInfo(new LaunchRequest(
            StarLauncherExe: "/p/x.exe", Runner: "/usr/bin/wine", WinePrefix: "/p",
            Esync: false, Fsync: false));
        Assert.Equal("0", wine.Environment["WINEESYNC"]);
        Assert.Equal("0", wine.Environment["WINEFSYNC"]);
        Assert.False(wine.Environment.ContainsKey("DXVK_HUD"));            // FPS off → unset
        Assert.False(wine.Environment.ContainsKey("STELLAR_PERFHUD"));     // FPS off → unset
        Assert.Equal("winhttp=n,b", wine.Environment["WINEDLLOVERRIDES"]); // nvapi off → not appended

        var proton = Linux().BuildStartInfo(new LaunchRequest(
            StarLauncherExe: "/p/x.exe", Runner: "/opt/GE-Proton10/proton", WinePrefix: "/p",
            Esync: false, Fsync: false));
        Assert.Equal("1", proton.Environment["PROTON_NO_ESYNC"]);
        Assert.Equal("1", proton.Environment["PROTON_NO_FSYNC"]);
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

    [Fact]
    public void Wrapper_wraps_runner_and_exe_in_order()
    {
        var psi = Linux().BuildStartInfo(new LaunchRequest(
            StarLauncherExe: "/p/drive_c/Star/StarLauncher/StarLauncher.exe",
            Runner: "/usr/bin/wine", WinePrefix: "/p",
            WrapperCommand: "gamescope -f --"));
        Assert.Equal("gamescope", psi.FileName);
        Assert.Equal("-f", psi.ArgumentList[0]);
        Assert.Equal("--", psi.ArgumentList[1]);
        Assert.Equal("/usr/bin/wine", psi.ArgumentList[2]);
        Assert.Contains("StarLauncher.exe", psi.ArgumentList[^1]);
    }

    [Fact]
    public void Game_arguments_are_appended_after_the_exe()
    {
        var psi = Linux().BuildStartInfo(new LaunchRequest(
            StarLauncherExe: "/p/drive_c/Star/StarLauncher/StarLauncher.exe",
            Runner: "/usr/bin/wine", WinePrefix: "/p",
            GameArguments: "--foo bar"));
        Assert.Equal("/usr/bin/wine", psi.FileName);
        Assert.Equal("bar", psi.ArgumentList[^1]);
        Assert.Equal("--foo", psi.ArgumentList[^2]);
    }

    [Fact]
    public void User_env_overrides_builtin()
    {
        var psi = Linux().BuildStartInfo(new LaunchRequest(
            StarLauncherExe: "/p/drive_c/Star/StarLauncher/StarLauncher.exe",
            Runner: "/usr/bin/wine", WinePrefix: "/p",
            Overlay: PerfOverlayMode.Full,
            ExtraEnv: new[] { new EnvVar { Name = "MANGOHUD", Value = "0" }, new EnvVar { Name = "MY_VAR", Value = "42" } }));
        Assert.Equal("0", psi.Environment["MANGOHUD"]);
        Assert.Equal("42", psi.Environment["MY_VAR"]);
    }

    [Fact]
    public void Winhttp_override_is_protected()
    {
        var psi = Linux().BuildStartInfo(new LaunchRequest(
            StarLauncherExe: "/p/drive_c/Star/StarLauncher/StarLauncher.exe",
            Runner: "/usr/bin/wine", WinePrefix: "/p",
            ExtraEnv: new[] { new EnvVar { Name = "WINEDLLOVERRIDES", Value = "d3d11=n,b" } }));
        var ov = psi.Environment["WINEDLLOVERRIDES"]!;
        Assert.Contains("d3d11=n,b", ov);
        Assert.Contains("winhttp=n,b", ov);
    }

    [Fact]
    public void Empty_named_env_var_is_skipped()
    {
        var psi = Linux().BuildStartInfo(new LaunchRequest(
            StarLauncherExe: "/p/drive_c/Star/StarLauncher/StarLauncher.exe",
            Runner: "/usr/bin/wine", WinePrefix: "/p",
            ExtraEnv: new[] { new EnvVar { Name = "", Value = "x" } }));
        Assert.False(psi.Environment.ContainsKey(""));
    }

    [Fact]
    public void No_advanced_fields_leaves_filename_as_runner()
    {
        var psi = Linux().BuildStartInfo(new LaunchRequest(
            StarLauncherExe: "/p/drive_c/Star/StarLauncher/StarLauncher.exe",
            Runner: "/usr/bin/wine", WinePrefix: "/p"));
        Assert.Equal("/usr/bin/wine", psi.FileName);   // wrapper not applied
    }
}

using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;
using Xunit;

public class LauncherSettingsViewModelTests
{
    private const string Main = "/opt/game/BlueProtocol/drive_c/Star/StarLauncher/game/release_3.7/game_mini";
    private sealed class Platform : IPlatformInfo { public bool IsWindows => false; public string AppDataDir => "/cfg"; }
    private sealed class Updates(string version) : ILauncherUpdateService
    {
        public Task<LauncherManifest> FetchAsync(Uri url, CancellationToken ct = default)
            => Task.FromResult(new LauncherManifest(version, "2026-09-14", "https://cdn/w.zip", "https://cdn/l.zip", null, "aa", "bb"));
    }
    private sealed class NoSelfUpdate : ILauncherSelfUpdater
    {
        public Task StageAsync(Stream zip, string sha, string staging, CancellationToken ct = default) => Task.CompletedTask;
        public string BuildWindowsSwapScript(string staging, string installDir, string exeName, int pid) => "";
        public string BuildUnixSwapScript(string staging, string installDir, string exeName, int pid) => "";
        public void ApplyAndRestart(string staging, string installDir, string exeName, bool isWindows) { }
        public void CleanupStaleUpdate(string installDir, string exeName) { }
    }

    private static (WorkspaceFixture f, LauncherSettingsViewModel vm) Open(string remoteVersion = "0.0.0")
    {
        var f = new WorkspaceFixture();
        f.AddClient("c1", "Main", Main);
        f.Start();
        var svc = new LauncherServices(new Updates(remoteVersion), new NoSelfUpdate(), new Platform(), f.Store, new HttpClient(), f.Fs);
        return (f, new LauncherSettingsViewModel(f.Shell, svc));
    }

    [Fact]
    public void Behaviour_switches_and_channel_persist()
    {
        var (f, vm) = Open();
        vm.TestingChannel = true; vm.KeepOpen = false; vm.StartOnLastClient = true; vm.ShowMatrix = false;
        var l = f.Store.Load().Launcher;
        Assert.Equal("testing", l.Channel); Assert.False(l.KeepOpen); Assert.Equal("lastClient", l.StartOn); Assert.False(l.ShowMatrix);
        Assert.Contains("v2 · 1 client", vm.FormatLine);
        Assert.Equal("/cfg/stellar-launcher/settings.json", vm.SettingsPath);
    }

    [Fact]
    public void Sources_add_validates_and_remove()
    {
        var (f, vm) = Open();
        vm.NewSource = "not a url"; vm.AddSourceCommand.Execute(null);
        Assert.Contains("valid", vm.Status); Assert.Empty(vm.Sources);
        vm.NewSource = "https://example.org/m.json"; vm.AddSourceCommand.Execute(null);
        Assert.Equal(new[] { "https://example.org/m.json" }, f.Store.Load().Launcher.PluginSources);
        vm.RemoveSourceCommand.Execute("https://example.org/m.json");
        Assert.Empty(f.Store.Load().Launcher.PluginSources);
    }

    [Fact]
    public async Task Check_updates_sets_the_rail_banner_only_for_a_newer_version()
    {
        var (f, vm) = Open("99.0.0");
        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        Assert.True(f.Shell.LauncherUpdateAvailable);
        Assert.Equal("↑ Launcher v99.0.0 available", f.Shell.LauncherUpdateText);
        Assert.Equal("v99.0.0 · 2026-09-14", vm.AvailableLabel);
    }

    [Fact]
    public async Task Check_updates_clears_the_banner_when_nothing_newer_exists()
    {
        var (f, vm) = Open("0.0.0");
        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        Assert.False(f.Shell.LauncherUpdateAvailable);
        Assert.Equal("", f.Shell.LauncherUpdateText);
    }

    [Fact]
    public void Export_and_import_round_trip_skipping_missing_folders_and_uniquing_names()
    {
        var (f, vm) = Open();
        f.Fs.AddDirectory("/tmp");                                     // a real save picker only returns existing folders
        vm.Export("/tmp/clients.json");
        Assert.StartsWith("exported 1 client", vm.Status);
        var json = f.Fs.File.ReadAllText("/tmp/clients.json");
        Assert.Contains("\"gameMiniDir\"", json);

        // an exported set from another machine: one exists here (Main → name clash), one doesn't
        f.Fs.AddDirectory("/opt/game/Other/drive_c/Star/StarLauncher/game/release_3.7/game_mini");
        f.Fs.AddFile("/tmp/import.json", new MockFileData("""
            [ { "id": "zz", "name": "Main", "accent": "#7c5cff", "gameMiniDir": "/opt/game/Other/drive_c/Star/StarLauncher/game/release_3.7/game_mini", "channel": "stable" },
              { "id": "yy", "name": "Gone", "accent": "#7c5cff", "gameMiniDir": "/nope/game_mini", "channel": "stable" } ]
            """));
        vm.Import("/tmp/import.json");
        var clients = f.Store.Load().Clients;
        Assert.Equal(2, clients.Count);
        Assert.Equal("Main 2", clients[1].Name);
        Assert.NotEqual("zz", clients[1].Id);                          // ids are reassigned
        Assert.Contains("skipped 1", vm.Status);
    }
}

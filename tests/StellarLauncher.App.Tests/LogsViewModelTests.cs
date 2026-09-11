using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.App.ViewModels.Workspace;
using StellarLauncher.Core.Logs;
using Xunit;

public class LogsViewModelTests
{
    private const string Test = "/opt/game/BlueProtocol2/drive_c/Star/StarLauncher/game/release_3.7/game_mini";
    private const string Log = "[Info   :   BepInEx] Loading [Stellar Framework 2.7.4]\n[Warning:   Stellar] [PluginRegistry] duplicate plugin id 'combatmeter'; second registration ignored.\n[Info   :   Stellar] [PluginRegistry] loaded 4 plugins\n[Error  :   Stellar] [CombatMeter] NullReferenceException\n";

    private static async Task<(WorkspaceFixture f, LogsViewModel vm, ClientWorkspaceViewModel ws)> Open()
    {
        var f = new WorkspaceFixture();
        f.Registry.Add(WorkspaceFixture.Entry("combatmeter", "CombatMeter", "2.0.0", "2.10.0"));
        f.AddClient("c2", "Test", Test, channel: "testing");
        f.InstallPlugin(Test, "combatmeter", "Stellar.CombatMeter.dll", "2.9.1");
        f.InstallPlugin(Test, "CombatMeter", "Stellar.CombatMeter.dll", "2.10.0");        // shadow copy
        f.Fs.AddFile($"{Test}/BepInEx/LogOutput.log", new MockFileData(Log));
        f.Tabs = f.Tabs with { Logs = w => new LogsViewModel(w) };
        f.Start();
        var ws = await f.OpenAsync("c2");
        ws.ShowLogsCommand.Execute(null);
        var vm = (LogsViewModel)ws.TabContent!;
        vm.Reload();
        return (f, vm, ws);
    }

    [Fact]
    public async Task Tail_filters_and_callout()
    {
        var (_, vm, ws) = await Open();
        Assert.Equal($"{Test}/BepInEx/LogOutput.log", vm.SelectedFile);
        Assert.Equal(4, vm.Lines.Count);
        Assert.True(vm.Lines[1].Highlight);                                      // the duplicate-id warning
        vm.SetFilterCommand.Execute(LogFilter.WarningsPlus);
        Assert.Equal(2, vm.Lines.Count);
        vm.Search = "NullReference";
        Assert.Single(vm.Lines);

        var c = Assert.Single(vm.Callouts);
        Assert.Equal(CalloutKind.DuplicatePluginSlot, c.Kind);
        Assert.True(c.FixAvailable);
        Assert.Equal(1, ws.LogsBadge);
        Assert.Contains("NullReference", vm.LastLinesText(1));
    }

    [Fact]
    public async Task Fix_collapses_the_duplicate_slot_outside_scan_paths_and_refreshes()
    {
        var (f, vm, ws) = await Open();
        await vm.Callouts[0].FixCommand.ExecuteAsync(null);
        Assert.True(f.Fs.Directory.Exists($"{Test}/stellar/plugins/CombatMeter"));      // 2.10.0 kept
        Assert.False(f.Fs.Directory.Exists($"{Test}/stellar/plugins/combatmeter"));
        Assert.Single(f.Fs.Directory.GetDirectories($"{Test}/stellar-backups"));
        Assert.Empty(ws.Callouts);
        Assert.Contains("kept", vm.Status);
    }

    [Fact]
    public async Task Timer_runs_only_while_active()
    {
        var (f, vm, _) = await Open();
        Assert.Empty(f.Ticks);
        vm.Activate(); Assert.Single(f.Ticks);
        vm.Deactivate(); Assert.Empty(f.Ticks);
    }

    [Fact]
    public async Task Disposing_the_tab_stops_its_timer()
    {
        var (f, vm, ws) = await Open();
        vm.Activate(); Assert.Single(f.Ticks);
        ws.Dispose();                                    // the shell disposes the page; the cascade must reach the timer
        Assert.Empty(f.Ticks);
    }
}

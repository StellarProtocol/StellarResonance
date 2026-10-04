using StellarLauncher.App.ViewModels;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using Xunit;

/// <summary>Task 6: one row of the plugin page's DEPENDENCIES section — where it goes, its state text and
/// pill class, and the use/skip toggle (optional only).</summary>
public class DependencyItemViewModelTests
{
    private static PluginDependency Dep(string target = "game", bool moddedOnly = false, bool optional = false) =>
        new("fx", "Effects", "6.8.0", "https://cdn/fx.zip", new string('a', 64), 1, "zip",
            new[] { new PluginDependencyFile("a.dll", "a.dll") }, target, ModdedOnly: moddedOnly, Optional: optional,
            License: "BSD-3-Clause", LicenseUrl: "https://example.org/license", SourceUrl: "https://example.org/src",
            Notice: "Use at your own discretion.");

    private static DependencyItemViewModel Vm(PluginDependency d, DependencyState state = DependencyState.Installed,
        string? detail = null, bool use = true, Action<string, bool>? setUse = null) =>
        new(d, new DependencyStatus(d.Id, state, detail), use, setUse ?? ((_, _) => { }));

    [Fact]
    public void Where_names_the_folder_and_the_modded_only_rule()
    {
        Assert.Equal("game folder · Modded launches only", Vm(Dep("game", moddedOnly: true)).Where);
        Assert.Equal("game folder", Vm(Dep("game")).Where);
        Assert.Equal("plugin folder", Vm(Dep("plugin")).Where);
    }

    [Theory]
    [InlineData(DependencyState.Installed, null, "Installed", "ok")]
    [InlineData(DependencyState.NotInstalled, null, "Not installed — installs at next Modded launch", "off")]
    [InlineData(DependencyState.Skipped, null, "Skipped", "off")]
    [InlineData(DependencyState.Blocked, "dxgi.dll", "Blocked — a file you installed is in the way: dxgi.dll", "warn")]
    [InlineData(DependencyState.Failed, "download timed out", "Failed: download timed out", "bad")]
    public void State_text_and_class_follow_the_dependency_state(DependencyState state, string? detail, string text, string cls)
    {
        var vm = Vm(Dep(), state, detail);

        Assert.Equal(text, vm.StateText);
        Assert.Equal(cls, vm.StateClass);
        Assert.Equal(cls == "ok", vm.IsOk);
        Assert.Equal(cls == "off", vm.IsOff);
        Assert.Equal(cls == "warn", vm.IsWarn);
        Assert.Equal(cls == "bad", vm.IsBad);
    }

    [Fact]
    public void Manifest_fields_are_shown_as_declared()
    {
        var vm = Vm(Dep(optional: true));

        Assert.Equal("Effects", vm.Name);
        Assert.Equal("6.8.0", vm.Version);
        Assert.Equal("BSD-3-Clause", vm.License);
        Assert.Equal("https://example.org/license", vm.LicenseUrl);
        Assert.Equal("https://example.org/src", vm.SourceUrl);
        Assert.Equal("Use at your own discretion.", vm.Notice);
        Assert.True(vm.IsOptional);
    }

    [Fact]
    public void Toggling_an_optional_dependency_calls_the_setter_with_its_id_and_value()
    {
        var calls = new List<(string, bool)>();
        var vm = Vm(Dep(optional: true), setUse: (id, use) => calls.Add((id, use)));

        vm.Use = false;
        vm.Use = true;

        Assert.Equal(new[] { ("fx", false), ("fx", true) }, calls);
        Assert.True(vm.Use);
    }

    [Fact]
    public void A_required_dependency_ignores_toggles()
    {
        var calls = 0;
        var vm = Vm(Dep(optional: false), setUse: (_, _) => calls++);

        vm.Use = false;

        Assert.Equal(0, calls);
        Assert.True(vm.Use);
    }

    [Fact]
    public void A_required_dependency_is_always_shown_as_used()
    {
        Assert.True(Vm(Dep(optional: false), DependencyState.Skipped, use: false).Use);
        Assert.False(Vm(Dep(optional: true), DependencyState.Skipped, use: false).Use);
    }

    [Fact]
    public void Update_refreshes_state_and_use_without_calling_the_setter()
    {
        var calls = 0;
        var vm = Vm(Dep(optional: true), DependencyState.NotInstalled, setUse: (_, _) => calls++);

        vm.Update(new DependencyStatus("fx", DependencyState.Skipped, null), use: false);

        Assert.Equal(0, calls);
        Assert.False(vm.Use);
        Assert.Equal("Skipped", vm.StateText);
    }

    // R-5: being moved aside is the NORMAL state after a Vanilla launch, not a problem — the neutral "off"
    // pill (the same outline NotInstalled/Skipped use), not the amber "warn" used for an actual Blocked
    // dependency row elsewhere on this page.
    [Theory]
    [InlineData(KeptDependencyDiskState.Kept, "Kept", "ok")]
    [InlineData(KeptDependencyDiskState.Parked, "Kept · moved aside", "off")]
    [InlineData(KeptDependencyDiskState.Missing, "Missing", "bad")]
    public void Kept_disk_state_sets_the_pill_text_and_class(KeptDependencyDiskState state, string text, string cls)
    {
        var vm = Vm(Dep());
        vm.ApplyKeptDiskState(state);

        Assert.Equal(text, vm.StateText);
        Assert.Equal(cls, vm.StateClass);
        Assert.Equal(cls == "ok", vm.IsOk);
        Assert.Equal(cls == "off", vm.IsOff);
        Assert.Equal(cls == "warn", vm.IsWarn);
        Assert.Equal(cls == "bad", vm.IsBad);
    }

    // Final review M-d: the row says WHO is in the way, and that a dependent of a blocked prerequisite waits.
    [Fact]
    public void Another_plugins_file_and_a_waiting_dependent_have_their_own_row_text()
    {
        var d = Dep();
        Assert.Equal("Blocked — another plugin's file is in the way: dxgi.dll",
            new DependencyItemViewModel(d, new DependencyStatus(d.Id, DependencyState.Blocked, "dxgi.dll", DependencyReason.OtherOwner), true, (_, _) => { }).StateText);
        Assert.Equal("Blocked — a file you installed is in the way: dxgi.dll",
            new DependencyItemViewModel(d, new DependencyStatus(d.Id, DependencyState.Blocked, "dxgi.dll", DependencyReason.PlayerFile), true, (_, _) => { }).StateText);
        Assert.Equal("Waiting for Effects runtime",
            new DependencyItemViewModel(d, new DependencyStatus(d.Id, DependencyState.Skipped, "Effects runtime", DependencyReason.WaitingForPrerequisite), true, (_, _) => { }).StateText);
    }
}

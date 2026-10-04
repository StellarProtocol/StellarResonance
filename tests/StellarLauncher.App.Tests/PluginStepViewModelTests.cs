using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels;
using Xunit;

/// <summary>v3 (spec § 12, mockup 2026-10-04 v3): the three steps' wording and defaults. Ticked by default (V1),
/// "Also reinstall dependencies" unticked by default (V2), "remove the plugin and its dependencies" by default (V3).</summary>
public class PluginStepViewModelTests
{
    private static readonly InstallStepOption Fx = new("fx", "ReShade 6.8.0 + Stellar ReShade bridge",
        "Post-processing effects. Goes in the game folder; Modded launches only. BSD-3-Clause / MIT.", "Use it at your own discretion.");

    [Fact]
    public void Install_step_uses_the_mockup_wording_and_ticks_every_option()
    {
        var vm = PluginStepViewModel.ForInstall(new InstallStep("Photo Studio", "1.5.0", new[] { Fx }));
        Assert.Equal("Install Photo Studio 1.5.0", vm.Title);
        Assert.Equal("Photo Studio works on its own. These optional extras add more; you can change them later on its page.", vm.Body);
        Assert.Equal("Install", vm.OkLabel);
        var row = Assert.Single(vm.Options);
        Assert.True(row.Use);
        Assert.Equal("Notice from Photo Studio: Use it at your own discretion.", row.NoticeText);
        Assert.Empty(vm.InstallResult().Unticked);
        row.Use = false;
        Assert.Equal(new[] { "fx" }, vm.InstallResult().Unticked);
    }

    [Fact]
    public void Reinstall_step_leaves_dependencies_unticked_by_default()
    {
        var vm = PluginStepViewModel.ForReinstall(new ReinstallStep("Photo Studio", "1.5.0", new[] { "ReShade", "Stellar ReShade bridge" }));
        Assert.Equal("Reinstall Photo Studio 1.5.0", vm.Title);
        Assert.Equal("Downloads Photo Studio again and checks it. Your settings and saved data are kept.", vm.Body);
        Assert.Equal("Downloads ReShade and Stellar ReShade bridge again and checks them. A file you put there yourself is never overwritten.", vm.ReinstallDetail);
        Assert.Equal("Reinstall", vm.OkLabel);
        Assert.False(vm.AlsoReinstallDependencies);
    }

    [Fact]
    public void Remove_step_defaults_to_removing_the_dependencies_too()
    {
        var vm = PluginStepViewModel.ForRemove(new RemoveStep("Photo Studio", new[] { "ReShade" }, Plural: false, AnyModdedOnly: true));
        Assert.Equal("Remove Photo Studio?", vm.Title);
        Assert.Equal("Your settings and saved data stay on disk either way.", vm.Body);
        Assert.Equal("Remove Photo Studio and its dependencies", vm.RemoveAllTitle);
        Assert.Equal("Also removes ReShade that the launcher installed for it.", vm.RemoveAllDetail);
        Assert.Equal("Remove Photo Studio only", vm.RemoveOnlyTitle);
        Assert.Equal("ReShade stays. The launcher keeps managing it (moved aside for Vanilla launches) and you can remove it later from this page.", vm.RemoveOnlyDetail);
        Assert.Equal(RemoveChoice.PluginAndDependencies, vm.RemoveResult());
        vm.KeepDependencies = true;
        Assert.False(vm.RemoveDependencies);
        Assert.Equal(RemoveChoice.PluginOnly, vm.RemoveResult());
    }

    [Fact]
    public void Remove_only_text_agrees_in_number_and_omits_parking_when_nothing_is_modded_only()
    {
        var vm = PluginStepViewModel.ForRemove(new RemoveStep("P", new[] { "a", "b" }, Plural: true, AnyModdedOnly: false));
        Assert.Equal("A and b stay. The launcher keeps managing them and you can remove them later from this page.", vm.RemoveOnlyDetail);
    }

    [Fact]
    public async Task Ok_completes_true_and_a_later_window_close_cannot_flip_it()
    {
        var vm = PluginStepViewModel.ForReinstall(new ReinstallStep("P", "1", new[] { "a" }));
        var closed = 0;
        vm.RequestClose += () => closed++;
        vm.OkCommand.Execute(null);
        vm.CancelIfUnfinished();
        Assert.True(await vm.Completion);
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task Cancel_or_closing_the_window_completes_false()
    {
        var a = PluginStepViewModel.ForRemove(new RemoveStep("P", new[] { "a" }, false, false));
        a.CancelCommand.Execute(null);
        Assert.False(await a.Completion);
        var b = PluginStepViewModel.ForRemove(new RemoveStep("P", new[] { "a" }, false, false));
        b.CancelIfUnfinished();   // title-bar X / Alt+F4
        Assert.False(await b.Completion);
    }

    [Theory]
    [InlineData(new[] { "A" }, "A")]
    [InlineData(new[] { "A", "B" }, "A and B")]
    [InlineData(new[] { "A", "B", "C" }, "A, B and C")]
    public void JoinNames_reads_as_a_list(string[] names, string expected) => Assert.Equal(expected, PluginStepText.JoinNames(names));
}

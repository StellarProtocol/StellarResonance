using StellarLauncher.App.Services;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Model;
using Xunit;

/// <summary>v3 V1/V2/V3: what each step lists — generic, from the manifest only.</summary>
public class PluginStepBuilderTests
{
    private static PluginDependency Dep(string id, string name, bool optional, string license = "MIT", string? description = null,
        string[]? requires = null, string? notice = null, bool modded = true) =>
        new(id, name, "6.8.0", $"https://cdn/{id}", new string('a', 64), 1, "file", new[] { new PluginDependencyFile(null, $"{id}.bin") },
            "game", ModdedOnly: modded, Optional: optional, Requires: requires, License: license, LicenseUrl: "https://l",
            SourceUrl: "https://s", Notice: notice, Description: description);

    private static PluginVersion V(string version, params PluginDependency[] deps) =>
        new(version, null, "P.dll", $"https://cdn/p/{version}.dll", "sha", "0.1.0", null, null, Dependencies: deps);

    private static readonly PluginDependency Fx = Dep("fx", "ReShade", true, "BSD-3-Clause", "Post-processing effects.", notice: "At your own discretion.");
    private static readonly PluginDependency Bridge = Dep("bridge", "Stellar ReShade bridge", false, requires: new[] { "fx" });

    [Fact]
    public void A_fresh_install_offers_every_optional_dependency_and_groups_its_required_dependents_on_one_row()
    {
        var entry = new PluginEntry("photo", "Photo Studio", "d", null, new[] { V("1.5.0", Fx, Bridge) });
        var offered = PluginStepBuilder.OfferedOptional(entry, entry.Versions[0], installedVersion: null);
        Assert.Equal(new[] { "fx" }, offered.Select(d => d.Id));

        var step = PluginStepBuilder.Install(entry, entry.Versions[0], offered);
        Assert.Equal("Photo Studio", step.PluginName);
        Assert.Equal("1.5.0", step.Version);
        var row = Assert.Single(step.Options);
        Assert.Equal("fx", row.DependencyId);
        Assert.Equal("ReShade 6.8.0 + Stellar ReShade bridge", row.Title);
        Assert.Equal("Post-processing effects. Goes in the game folder; Modded launches only. BSD-3-Clause / MIT.", row.Detail);
        Assert.Equal("At your own discretion.", row.Notice);
    }

    [Fact]
    public void An_update_offers_only_optional_dependencies_the_installed_version_did_not_declare()
    {
        var lut = Dep("lut", "Colour tables", true);
        var entry = new PluginEntry("photo", "Photo Studio", "d", null, new[] { V("2.0.0", Fx, Bridge, lut), V("1.5.0", Fx, Bridge) });
        Assert.Equal(new[] { "lut" }, PluginStepBuilder.OfferedOptional(entry, entry.Versions[0], "1.5.0").Select(d => d.Id));
        Assert.Empty(PluginStepBuilder.OfferedOptional(entry, entry.Versions[1], "1.5.0"));
    }

    [Fact]
    public void Required_only_or_no_dependencies_offer_nothing()
    {
        var entry = new PluginEntry("p", "P", "d", null, new[] { V("1.0.0", Dep("req", "Req", false)), V("0.9.0") });
        Assert.Empty(PluginStepBuilder.OfferedOptional(entry, entry.Versions[0], null));
        Assert.Empty(PluginStepBuilder.OfferedOptional(entry, entry.Versions[1], null));
    }

    [Fact]
    public void The_choice_rewrites_only_the_offered_entries_of_this_plugin()
    {
        var c = new ClientProfile { SkippedDependencies = { "photo/fx", "photo/old", "other/fx" } };
        var lut = Dep("lut", "Colour tables", true);
        PluginStepBuilder.ApplyInstallChoice(c, "photo", new[] { Fx, lut }, new HashSet<string> { "lut" });
        Assert.Equal(new[] { "other/fx", "photo/lut", "photo/old" }, c.SkippedDependencies.OrderBy(s => s));
    }

    [Fact]
    public void Where_reads_as_a_sentence()
    {
        Assert.Equal("Goes in the game folder; Modded launches only.", PluginStepBuilder.WhereSentence(Fx));
        Assert.Equal("Goes in the game folder.", PluginStepBuilder.WhereSentence(Dep("g", "G", true, modded: false)));
        Assert.Equal("Goes in the plugin's own folder.", PluginStepBuilder.WhereSentence(Dep("p", "P", true, modded: false) with { Target = "plugin" }));
    }

    // Review fix round 2 (b): a returning player's earlier opt-out reads back as unticked.
    [Fact]
    public void Install_marks_a_previously_skipped_offered_dependency_as_initially_unticked()
    {
        var entry = new PluginEntry("photo", "Photo Studio", "d", null, new[] { V("1.5.0", Fx, Bridge) });
        var offered = PluginStepBuilder.OfferedOptional(entry, entry.Versions[0], installedVersion: null);
        var client = new ClientProfile { SkippedDependencies = { "photo/fx" } };

        var step = PluginStepBuilder.Install(entry, entry.Versions[0], offered, client);

        Assert.True(Assert.Single(step.Options).InitiallyUnticked);
    }

    [Fact]
    public void Install_without_a_client_defaults_every_row_to_not_initially_unticked()
    {
        var entry = new PluginEntry("photo", "Photo Studio", "d", null, new[] { V("1.5.0", Fx, Bridge) });
        var offered = PluginStepBuilder.OfferedOptional(entry, entry.Versions[0], installedVersion: null);

        var step = PluginStepBuilder.Install(entry, entry.Versions[0], offered);

        Assert.False(Assert.Single(step.Options).InitiallyUnticked);
    }

    [Fact]
    public void The_reinstall_step_names_the_used_dependencies_and_is_skipped_when_every_one_is_skipped()
    {
        var entry = new PluginEntry("photo", "Photo Studio", "d", null, new[] { V("1.5.0", Fx) });
        var step = PluginStepBuilder.Reinstall(new ClientProfile(), entry, entry.Versions[0]);
        Assert.Equal(new[] { "ReShade" }, step!.DependencyNames);
        Assert.Null(PluginStepBuilder.Reinstall(new ClientProfile { SkippedDependencies = { "photo/fx" } }, entry, entry.Versions[0]));
    }

    [Fact]
    public void The_remove_step_names_used_dependencies_with_number_and_parking()
    {
        var entry = new PluginEntry("photo", "Photo Studio", "d", null, new[] { V("1.5.0", Fx, Bridge) });
        var both = PluginStepBuilder.Remove(new ClientProfile(), entry, entry.Versions[0].Dependencies!);
        Assert.Equal(new[] { "ReShade", "Stellar ReShade bridge" }, both.DependencyNames);
        Assert.True(both.Plural);
        Assert.True(both.AnyModdedOnly);
        var none = PluginStepBuilder.Remove(new ClientProfile { SkippedDependencies = { "photo/fx" } }, entry, new[] { Fx });
        Assert.Equal(new[] { "the dependencies" }, none.DependencyNames);
        Assert.True(none.Plural);
    }
}

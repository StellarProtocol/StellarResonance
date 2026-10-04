using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StellarLauncher.App.Services;

/// <summary>v3 (spec § 12): the install / reinstall / remove step a plugin with dependencies goes through — a modal
/// dialog in the app (<c>Views.PluginStepDialog</c>), scripted answers in tests. Every method returns null on Cancel
/// (or when the window is closed), and the caller then changes nothing.</summary>
public interface IPluginSteps
{
    Task<InstallStepResult?> AskInstallAsync(InstallStep step);
    /// <returns>null = Cancel; true = "Also reinstall dependencies" ticked.</returns>
    Task<bool?> AskReinstallAsync(ReinstallStep step);
    Task<RemoveChoice?> AskRemoveAsync(RemoveStep step);
}

/// <summary>One install-step row: an optional dependency, plus the required ones that need it (they share its checkbox).</summary>
public sealed record InstallStepOption(string DependencyId, string Title, string Detail, string? Notice);
public sealed record InstallStep(string PluginName, string Version, IReadOnlyList<InstallStepOption> Options);
/// <summary>The ids (of <see cref="InstallStepOption.DependencyId"/>) the player unticked.</summary>
public sealed record InstallStepResult(IReadOnlySet<string> Unticked);
public sealed record ReinstallStep(string PluginName, string Version, IReadOnlyList<string> DependencyNames);
/// <param name="Plural">Agreement for the "… stay(s)" sentence (false only for exactly one named dependency).</param>
/// <param name="AnyModdedOnly">Whether "moved aside for Vanilla launches" applies to what is kept.</param>
public sealed record RemoveStep(string PluginName, IReadOnlyList<string> DependencyNames, bool Plural, bool AnyModdedOnly);
public enum RemoveChoice { PluginAndDependencies, PluginOnly }

public static class PluginStepText
{
    /// <summary>"A", "A and B", "A, B and C".</summary>
    public static string JoinNames(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    /// <summary>Upper-cases the first character (a list of names starting a sentence).</summary>
    public static string Sentence(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

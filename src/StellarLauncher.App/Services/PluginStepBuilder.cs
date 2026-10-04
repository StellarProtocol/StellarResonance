using System;
using System.Collections.Generic;
using System.Linq;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Model;

namespace StellarLauncher.App.Services;

/// <summary>v3 (spec § 12): what each plugin step lists. Generic — only what the manifest declares.</summary>
public static class PluginStepBuilder
{
    /// <summary>V1: the optional dependencies an install of <paramref name="target"/> asks about — every one on a fresh
    /// install; on an update/downgrade only those the installed version did not declare (the player already chose about
    /// the rest, and that choice is in SkippedDependencies).</summary>
    public static IReadOnlyList<PluginDependency> OfferedOptional(PluginEntry entry, PluginVersion target, string? installedVersion)
    {
        var known = (installedVersion is null ? null : entry.Versions.FirstOrDefault(v => v.Version == installedVersion))
            ?.Dependencies?.Select(d => d.Id).ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        return (target.Dependencies ?? Array.Empty<PluginDependency>()).Where(d => d.Optional && !known.Contains(d.Id)).ToList();
    }

    /// <summary>Review fix round 2 (b): <paramref name="client"/> is optional (additive — every existing call site
    /// compiles unchanged) so each offered row can be pre-unticked from the client's current <c>SkippedDependencies</c>,
    /// a returning player's earlier opt-out reading back as unticked instead of defaulting to ticked again.</summary>
    public static InstallStep Install(PluginEntry entry, PluginVersion target, IReadOnlyList<PluginDependency> offered, ClientProfile? client = null)
    {
        var all = target.Dependencies ?? Array.Empty<PluginDependency>();
        var alreadySkipped = client is null ? null : DependencyRunner.Skipped(client, entry.Id, offered);
        return new InstallStep(entry.Name, target.Version, offered.Select(o => Option(o, all, alreadySkipped?.Contains(o.Id) == true)).ToList());
    }

    /// <summary>An optional dependency plus every REQUIRED one that needs it (directly or through another): unticking it
    /// skips those too (the requires gate), so they share its row — "&lt;name&gt; &lt;version&gt; + &lt;names&gt;".</summary>
    private static InstallStepOption Option(PluginDependency o, IReadOnlyList<PluginDependency> all, bool initiallyUnticked)
    {
        var group = new List<PluginDependency> { o };
        var ids = new HashSet<string>(StringComparer.Ordinal) { o.Id };
        foreach (var d in all)   // manifest order: a prerequisite is listed before its dependents
            if (!d.Optional && d.Requires?.Any(ids.Contains) == true && ids.Add(d.Id)) group.Add(d);

        var title = string.Join(" + ", group.Select((d, i) => i == 0 ? $"{d.Name} {d.Version}" : d.Name));
        var description = group.Select(d => d.Description?.Trim()).FirstOrDefault(s => !string.IsNullOrEmpty(s));
        var licences = string.Join(" / ", group.Select(d => d.License).Where(l => !string.IsNullOrWhiteSpace(l)).Distinct());
        var detail = string.Join(" ", new[] { description, WhereSentence(o), licences.Length > 0 ? licences + "." : null }
            .Where(s => !string.IsNullOrEmpty(s)));
        var notice = string.Join(" ", group.Select(d => d.Notice?.Trim()).Where(n => !string.IsNullOrEmpty(n)).Distinct());
        return new InstallStepOption(o.Id, title, detail, notice.Length == 0 ? null : notice, initiallyUnticked);
    }

    public static string WhereSentence(PluginDependency d) =>
        string.Equals(d.Target, "game", StringComparison.OrdinalIgnoreCase)
            ? (d.ModdedOnly ? "Goes in the game folder; Modded launches only." : "Goes in the game folder.")
            : "Goes in the plugin's own folder.";

    /// <summary>V1: records the step's answer for the OFFERED dependencies only (others keep their earlier choice).</summary>
    public static void ApplyInstallChoice(ClientProfile client, string pluginId, IReadOnlyList<PluginDependency> offered,
        IReadOnlySet<string> unticked)
    {
        foreach (var d in offered)
        {
            var key = $"{pluginId}/{d.Id}";
            client.SkippedDependencies.RemoveAll(s => s == key);
            if (unticked.Contains(d.Id)) client.SkippedDependencies.Add(key);
        }
    }

    /// <summary>V2: null when there is nothing to reinstall (no dependency, or every one skipped) — one-click then. Review
    /// fix round 1 (c): this IS the "skip the step with zero dependency names" guard for reinstall — the caller
    /// (<see cref="PluginInstallFlow"/>) only asks when this returns non-null.</summary>
    public static ReinstallStep? Reinstall(ClientProfile client, PluginEntry entry, PluginVersion target)
    {
        var deps = target.Dependencies ?? Array.Empty<PluginDependency>();
        var skipped = DependencyRunner.Skipped(client, entry.Id, deps);
        var used = deps.Where(d => !skipped.Contains(d.Id)).Select(d => d.Name).ToList();
        return used.Count == 0 ? null : new ReinstallStep(entry.Name, target.Version, used);
    }

    /// <summary>V3: the names the remove step shows — the declared, non-skipped dependencies ("the dependencies" when the
    /// ledger holds only ones this version no longer names). Used by Task 6. Review fix round 1 (c): this always returns
    /// a step when <paramref name="declared"/> is non-empty (even if every one is currently skipped — the player may
    /// still want to remove what the ledger kept); its CALLER must be the one to skip the step entirely when the plugin
    /// never declared any dependency at all (<paramref name="declared"/> empty) — there is nothing to ask about then.</summary>
    public static RemoveStep Remove(ClientProfile client, PluginEntry entry, IReadOnlyList<PluginDependency> declared)
    {
        var skipped = DependencyRunner.Skipped(client, entry.Id, declared);
        var used = declared.Where(d => !skipped.Contains(d.Id)).ToList();
        IReadOnlyList<string> names = used.Count > 0 ? used.Select(d => d.Name).ToList() : new[] { "the dependencies" };
        return new RemoveStep(entry.Name, names, Plural: used.Count != 1, AnyModdedOnly: used.Any(d => d.ModdedOnly));
    }
}

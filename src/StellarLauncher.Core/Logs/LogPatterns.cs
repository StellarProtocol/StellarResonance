using System.Collections.Generic;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Localization;

namespace StellarLauncher.Core.Logs;

public enum CalloutKind { DuplicatePluginSlot, FrameworkShadowCopy, GamePatchedNewerRelease }

public sealed record LogCallout(CalloutKind Kind, string Title, string Detail, bool FixAvailable,
    DuplicateSlot? Slot = null, string? Path = null);

/// <summary>The v1 recognised failure classes (spec § 9). Facts come from the folder (inventory) so a
/// callout is never a false positive from an old log line.</summary>
public static class LogPatterns
{
    public static IReadOnlyList<LogCallout> Scan(IReadOnlyList<LogLine> lines, InventorySnapshot inv,
        IReadOnlyList<string> frameworkShadowDirs, string? newerGameMini)
    {
        var result = new List<LogCallout>();
        foreach (var slot in inv.DuplicateSlots)
            result.Add(new LogCallout(CalloutKind.DuplicatePluginSlot,
                L.TFormat("callout.dupSlot.title", slot.Key),
                L.TFormat("callout.dupSlot.detail", string.Join(L.T("callout.and"), slot.Dirs)),
                FixAvailable: true, Slot: slot));

        if (frameworkShadowDirs.Count > 0)
            result.Add(new LogCallout(CalloutKind.FrameworkShadowCopy,
                L.T("callout.shadow.title"),
                L.TFormat("callout.shadow.detail", string.Join(", ", frameworkShadowDirs)),
                FixAvailable: true));

        if (newerGameMini is not null)
            result.Add(new LogCallout(CalloutKind.GamePatchedNewerRelease,
                L.T("callout.newerRelease.title"),
                L.TFormat("callout.newerRelease.detail", newerGameMini),
                FixAvailable: true, Path: newerGameMini));
        return result;
    }
}

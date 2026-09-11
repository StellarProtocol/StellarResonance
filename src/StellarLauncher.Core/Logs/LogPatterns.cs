using System.Collections.Generic;
using StellarLauncher.Core.Inventory;

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
                $"Two copies of '{slot.Key}' in stellar/plugins",
                $"{string.Join(" and ", slot.Dirs)} — the framework loads one of them arbitrarily, so a fix you install can end up not running. Collapsing keeps the newer build and moves the other to stellar-backups/; plugin data is untouched (keyed by GUID).",
                FixAvailable: true, Slot: slot));

        if (frameworkShadowDirs.Count > 0)
            result.Add(new LogCallout(CalloutKind.FrameworkShadowCopy,
                "A second framework copy is shadowing the deployed one",
                $"{string.Join(", ", frameworkShadowDirs)} also carries a .stellar-version marker under BepInEx/plugins; BepInEx keeps one per version arbitrarily (log shows 'Skipping [...] newer version exists'). Evacuate moves it to stellar-backups/.",
                FixAvailable: true));

        if (newerGameMini is not null)
            result.Add(new LogCallout(CalloutKind.GamePatchedNewerRelease,
                "The game patched to a newer release folder",
                $"Detected {newerGameMini}; this client still points at an older release_<ver>. Re-point the client to launch the patched game.",
                FixAvailable: true, Path: newerGameMini));
        return result;
    }
}

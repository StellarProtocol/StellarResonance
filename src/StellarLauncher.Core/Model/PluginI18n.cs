using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using StellarLauncher.Core.Localization;

namespace StellarLauncher.Core.Model;

/// <summary>
/// Per-language plugin presentation from the registry (launcher i18n spec § C): each field is picked for the launcher's
/// active language and falls back to the English top-level field PER FIELD — a missing, empty, null or malformed
/// translation never hides the English text and never throws. <c>en</c> (or any unsupported code) is plain English.
/// </summary>
public static class PluginI18n
{
    /// <summary>Display name in <paramref name="lang"/>, else <see cref="PluginEntry.Name"/>.</summary>
    public static string DisplayName(this PluginEntry e, string lang) => Text(Block(e.I18n, lang), "name") ?? e.Name;

    /// <summary>Description in <paramref name="lang"/>, else <see cref="PluginEntry.Description"/>.</summary>
    public static string DisplayDescription(this PluginEntry e, string lang) => Text(Block(e.I18n, lang), "description") ?? e.Description;

    /// <summary>Caption of media item <paramref name="index"/>: the translated caption (by index; JSON null =
    /// untranslated) when the English item has a caption, else the English caption.</summary>
    public static string? MediaCaption(this PluginEntry e, int index, string lang)
    {
        var english = e.Media is { } m && index >= 0 && index < m.Count ? m[index]?.Caption : null;
        if (string.IsNullOrWhiteSpace(english)) return english;
        if (Block(e.I18n, lang) is { } b && b.TryGetProperty("captions", out var caps) && caps.ValueKind == JsonValueKind.Array
            && index < caps.GetArrayLength() && caps[index] is { ValueKind: JsonValueKind.String } c && !string.IsNullOrWhiteSpace(c.GetString()))
            return c.GetString();
        return english;
    }

    /// <summary>The guide to show: <c>guideUrls[lang]</c> when published, else <see cref="PluginEntry.GuideUrl"/>.</summary>
    public static string? GuideUrlFor(this PluginEntry e, string lang) =>
        e.GuideUrl is not null && IsTranslatable(lang) && e.GuideUrls is { ValueKind: JsonValueKind.Object } g
        && g.TryGetProperty(lang, out var u) && u.ValueKind == JsonValueKind.String
        && Uri.TryCreate(u.GetString(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? u.GetString()
            : e.GuideUrl;

    /// <summary>This version's changelog in <paramref name="lang"/>, section by section: a translated section that is a
    /// non-empty list replaces the English one; any other section stays English. Null when there is no English changelog.</summary>
    public static Changelog? ChangelogFor(this PluginVersion v, string lang)
    {
        if (v.Changelog is not { } en) return null;
        if (Block(v.ChangelogI18n, lang) is not { } tr) return en;
        return new Changelog(Section(tr, "added", en.Added), Section(tr, "changed", en.Changed),
            Section(tr, "fixed", en.Fixed), Section(tr, "removed", en.Removed));
    }

    private static IReadOnlyList<string> Section(JsonElement tr, string name, IReadOnlyList<string>? english)
    {
        if (tr.TryGetProperty(name, out var s) && s.ValueKind == JsonValueKind.Array && s.GetArrayLength() > 0
            && s.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String))
            return s.EnumerateArray().Select(x => x.GetString()!).ToList();
        return english ?? Array.Empty<string>();
    }

    private static bool IsTranslatable(string lang) => lang != LauncherLanguages.English && LauncherLanguages.IsSupported(lang);

    private static JsonElement? Block(JsonElement? root, string lang) =>
        IsTranslatable(lang) && root is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty(lang, out var b)
        && b.ValueKind == JsonValueKind.Object ? b : null;

    private static string? Text(JsonElement? block, string key) =>
        block is { } b && b.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString() : null;
}

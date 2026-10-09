using System;
using System.Collections.Generic;
using System.Linq;

namespace StellarLauncher.Core.Localization;

/// <summary>
/// The ONE list of launcher UI languages (mirrors the framework's <c>UiLanguages</c>). The service's supported set,
/// the embedded catalogs and the Settings dropdown all read this, so a language cannot be half-added.
/// Order = dropdown order.
/// </summary>
public static class LauncherLanguages
{
    /// <summary>The persisted setting value meaning "follow the OS UI language".</summary>
    public const string Follow = "follow";

    /// <summary>The source + fallback language.</summary>
    public const string English = "en";

    /// <summary>Supported language codes, in dropdown order.</summary>
    public static IReadOnlyList<string> Codes { get; } = new[] { "en", "ja", "th", "id", "fil", "ko" };

    /// <summary>Each language's name in its own script, index-aligned to <see cref="Codes"/>. Never translated.</summary>
    public static IReadOnlyList<string> NativeNames { get; } =
        new[] { "English", "日本語", "ไทย", "Bahasa Indonesia", "Filipino", "한국어" };

    /// <summary>True for a supported language code (case-sensitive, lowercase).</summary>
    public static bool IsSupported(string? code) => code is not null && Codes.Contains(code, StringComparer.Ordinal);

    /// <summary>True for a valid persisted setting: <see cref="Follow"/> or a supported code.</summary>
    public static bool IsValidSetting(string? setting) => setting == Follow || IsSupported(setting);

    /// <summary>The native name of <paramref name="code"/> (English for an unknown code).</summary>
    public static string NativeName(string code)
    {
        for (var i = 0; i < Codes.Count; i++)
            if (Codes[i] == code) return NativeNames[i];
        return NativeNames[0];
    }

    /// <summary>
    /// Map an OS UI culture's two-letter ISO language name to a supported code: legacy/alias codes
    /// <c>tl</c> (Tagalog) → <c>fil</c> and <c>in</c> (old Indonesian) → <c>id</c>; anything unsupported → <c>en</c>.
    /// </summary>
    public static string FromCulture(string? twoLetterIsoName)
    {
        var c = (twoLetterIsoName ?? "").Trim().ToLowerInvariant();
        c = c switch { "tl" => "fil", "in" => "id", _ => c };
        return IsSupported(c) ? c : English;
    }
}

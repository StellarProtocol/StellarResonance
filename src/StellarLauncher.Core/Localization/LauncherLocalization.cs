using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace StellarLauncher.Core.Localization;

/// <summary>
/// The launcher's localization service — mirrors the framework's <c>LocalizationEngine</c>: one flat catalog per
/// language (embedded <c>Lang/&lt;code&gt;.json</c>), resolution active → English → key literal, <c>string.Format</c>
/// support and a <see cref="LanguageChanged"/> event for live switching. <c>follow</c> resolves the OS UI culture once
/// at construction (see <see cref="LauncherLanguages.FromCulture"/>). Pure managed; persisting the setting is the
/// caller's job (it lives in <c>LauncherOptions.Language</c>).
/// </summary>
public sealed class LauncherLocalization : ILauncherLocalization
{
    private const string ResourcePrefix = "StellarLauncher.Lang.";

    private readonly Dictionary<string, Dictionary<string, string>> _catalogs = new(StringComparer.Ordinal);
    private readonly string _followResolved;

    /// <summary>Build from the embedded catalogs; <paramref name="osLanguage"/> is the OS UI culture's two-letter
    /// ISO name (defaults to <see cref="CultureInfo.CurrentUICulture"/>).</summary>
    public LauncherLocalization(string? setting, Func<string>? osLanguage = null)
        : this(setting, osLanguage, EmbeddedCatalogs()) { }

    /// <summary>Build from explicit catalogs (code → JSON object of key → string). Used by tests.</summary>
    public LauncherLocalization(string? setting, Func<string>? osLanguage, IReadOnlyDictionary<string, string> catalogJson)
    {
        foreach (var (code, json) in catalogJson) _catalogs[code] = Parse(json);
        _followResolved = LauncherLanguages.FromCulture((osLanguage ?? CurrentUiLanguage)());
        Setting = LauncherLanguages.IsValidSetting(setting) ? setting! : LauncherLanguages.Follow;
    }

    /// <inheritdoc/>
    public string Setting { get; private set; }

    /// <inheritdoc/>
    public string ActiveLanguage => Setting == LauncherLanguages.Follow ? _followResolved : Setting;

    /// <inheritdoc/>
    public string FollowLanguage => _followResolved;

    /// <inheritdoc/>
    public event Action? LanguageChanged;

    /// <inheritdoc/>
    public string T(string key) => TryResolve(key, out var v) ? v : key;

    /// <inheritdoc/>
    public string TFormat(string key, params object?[] args)
    {
        if (!TryResolve(key, out var template)) return key;
        try { return string.Format(template, args); }
        catch (FormatException) { return template; }
    }

    /// <inheritdoc/>
    public void SetLanguage(string setting)
    {
        if (!LauncherLanguages.IsValidSetting(setting) || setting == Setting) return;
        var oldActive = ActiveLanguage;
        Setting = setting;
        if (ActiveLanguage != oldActive) RaiseLanguageChanged();
    }

    /// <summary>The embedded catalogs, code → raw JSON, for every listed language that ships one.</summary>
    public static IReadOnlyDictionary<string, string> EmbeddedCatalogs()
    {
        var asm = typeof(LauncherLocalization).Assembly;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var code in LauncherLanguages.Codes)
        {
            using var s = asm.GetManifestResourceStream(ResourcePrefix + code + ".json");
            if (s is null) continue;
            using var r = new StreamReader(s);
            result[code] = r.ReadToEnd();
        }
        return result;
    }

    // One throwing subscriber must not starve the others (every bound label re-resolves through this event).
    private void RaiseLanguageChanged()
    {
        var handlers = LanguageChanged;
        if (handlers is null) return;
        foreach (var d in handlers.GetInvocationList())
        {
            try { ((Action)d)(); }
            catch (Exception) { /* a broken subscriber must not block the rest of the UI from switching */ }
        }
    }

    private bool TryResolve(string key, out string value)
    {
        if (_catalogs.TryGetValue(ActiveLanguage, out var active) && active.TryGetValue(key, out value!)) return true;
        if (_catalogs.TryGetValue(LauncherLanguages.English, out var en) && en.TryGetValue(key, out value!)) return true;
        value = key;
        return false;
    }

    private static string CurrentUiLanguage() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    private static Dictionary<string, string> Parse(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }
}

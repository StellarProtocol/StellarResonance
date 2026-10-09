using System;

namespace StellarLauncher.Core.Localization;

/// <summary>The launcher's own UI localization: catalog lookup, the language setting and its live-change event.</summary>
public interface ILauncherLocalization
{
    /// <summary>Raw setting: <see cref="LauncherLanguages.Follow"/> or a supported code.</summary>
    string Setting { get; }

    /// <summary>Resolved active language — always one of <see cref="LauncherLanguages.Codes"/>.</summary>
    string ActiveLanguage { get; }

    /// <summary>The language <c>follow</c> resolves to on this machine (shown in the "Follow system (…)" option).</summary>
    string FollowLanguage { get; }

    /// <summary>Raised (on the caller's thread) after the ACTIVE language changes.</summary>
    event Action? LanguageChanged;

    /// <summary>Resolve <paramref name="key"/>: active language → English → the key literal.</summary>
    string T(string key);

    /// <summary><see cref="T"/> then <c>string.Format</c>; a missing key returns the key literal, a bad template returns
    /// the template unformatted.</summary>
    string TFormat(string key, params object?[] args);

    /// <summary>Change the setting (<c>follow</c> or a supported code). Invalid values are ignored. Fires
    /// <see cref="LanguageChanged"/> only when the active language actually changes. Persisting is the caller's job.</summary>
    void SetLanguage(string setting);
}

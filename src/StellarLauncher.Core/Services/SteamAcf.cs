using System;

namespace StellarLauncher.Core.Services;

/// <summary>
/// Minimal reader for Steam's <c>appmanifest_&lt;id&gt;.acf</c> (VDF) files. We only need two keys —
/// <c>appid</c> and <c>installdir</c> — to map a game folder back to its Steam app id so the launcher
/// can start it via <c>steam://rungameid/&lt;id&gt;</c> instead of running the exe directly.
/// </summary>
public static class SteamAcf
{
    /// <summary>The app id if this manifest's installdir matches <paramref name="installDirName"/>, else null.</summary>
    public static string? AppIdForInstallDir(string acfText, string installDirName)
    {
        if (string.IsNullOrEmpty(acfText) || string.IsNullOrEmpty(installDirName)) return null;
        var appId = QuotedValue(acfText, "appid");
        var installDir = QuotedValue(acfText, "installdir");
        if (appId is null || installDir is null) return null;
        return string.Equals(installDir, installDirName, StringComparison.OrdinalIgnoreCase) ? appId : null;
    }

    // VDF is "key"\t\t"value"; pull the first quoted token after the first occurrence of "key".
    private static string? QuotedValue(string acf, string key)
    {
        var marker = "\"" + key + "\"";
        var i = acf.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var q1 = acf.IndexOf('"', i + marker.Length);
        if (q1 < 0) return null;
        var q2 = acf.IndexOf('"', q1 + 1);
        if (q2 < 0) return null;
        return acf.Substring(q1 + 1, q2 - q1 - 1);
    }
}

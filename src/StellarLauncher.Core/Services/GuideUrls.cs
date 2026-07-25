using System;

namespace StellarLauncher.Core.Services;

// Resolves image/link targets inside a plugin guide against the guide's own URL, so guides
// reference their media relatively ("media/shot.png") and never hard-code a CDN base or
// plugin id. The same relative layout works in the registry repo (GitHub rendering), on the
// curated CDN, and in any third-party registry. Only http(s) results are ever returned.
public static class GuideUrls
{
    public static string? Resolve(string? baseUrl, string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        Uri? result;
        if (Uri.TryCreate(target, UriKind.Absolute, out var abs)) result = abs;
        else if (baseUrl is not null
                 && Uri.TryCreate(baseUrl, UriKind.Absolute, out var b)
                 && Uri.TryCreate(b, target, out var rel)) result = rel;
        else return null;
        return result.Scheme is "http" or "https" ? result.AbsoluteUri : null;
    }
}

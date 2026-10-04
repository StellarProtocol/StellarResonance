using System;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Dependencies;

/// <summary>The launcher's own check of one dependency declaration, mirroring the registry validator
/// (<c>tools/build-registry.py</c>) so a manifest from any registry is held to the same rules before anything
/// is downloaded (final review I5, M-a, M-b; docs/manifest-standard.md § 3.2).</summary>
public static class DependencyDeclaration
{
    /// <summary>The registry's <c>MAX_DEPENDENCY_BYTES</c>: a hard ceiling on a download, whatever the
    /// manifest's <c>size</c> says (M-a).</summary>
    public const long MaxBytes = 512L * 1024 * 1024;

    /// <summary>Why <paramref name="d"/> can't be installed, or null when it is acceptable: an id outside
    /// [A-Za-z0-9._-], a non-https download URL, a missing <c>license</c>/<c>licenseUrl</c>/<c>sourceUrl</c>,
    /// or a declared size over <see cref="MaxBytes"/>.</summary>
    public static string? Problem(PluginDependency d)
    {
        if (!DependencyPaths.IsValidPluginId(d.Id)) return "invalid dependency id";   // same charset as a plugin id
        if (!Uri.TryCreate(d.Url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
            return "download URL must be https";
        if (string.IsNullOrWhiteSpace(d.License)) return "no license declared";
        if (string.IsNullOrWhiteSpace(d.LicenseUrl)) return "no licenseUrl declared";
        if (string.IsNullOrWhiteSpace(d.SourceUrl)) return "no sourceUrl declared";
        if (d.Size > MaxBytes) return "larger than the 512 MiB limit";
        return null;
    }
}

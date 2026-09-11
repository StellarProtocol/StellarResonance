using System;
using System.IO;
using System.Linq;
using StellarLauncher.Core.Services;

namespace StellarLauncher.Core.Model;

public static class PluginEntryExtensions
{
    /// <summary>Canonical on-disk DLL name: the newest version's <c>dll</c>, else the file name of its URL.</summary>
    public static string? CanonicalDll(this PluginEntry e)
    {
        if (e.Versions is null || e.Versions.Count == 0) return null;
        var v = e.Versions[0];
        if (!string.IsNullOrEmpty(v.Dll)) return v.Dll;
        return Uri.TryCreate(v.DllUrl, UriKind.Absolute, out var u) ? Path.GetFileName(u.LocalPath) : null;
    }

    /// <summary>Newest registry version that runs on <paramref name="framework"/> (Versions are newest-first).</summary>
    public static PluginVersion? BestCompatible(this PluginEntry e, string? framework) =>
        framework is null ? null
            : e.Versions.FirstOrDefault(v => VersionService.IsModSystemCompatible(framework, v.MinModSystemVersion, v.MaxModSystemVersion));
}

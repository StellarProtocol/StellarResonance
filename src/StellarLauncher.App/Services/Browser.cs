using System;
using System.Diagnostics;

namespace StellarLauncher.App.Services;

// Opens http(s) links in the user's default browser. Anything else (file:, custom schemes)
// is refused — registry/guide content must never launch arbitrary programs.
internal static class Browser
{
    public static void Open(string? url)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme is not ("http" or "https")) return;
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", uri.AbsoluteUri);
            else
                Process.Start("xdg-open", uri.AbsoluteUri);
        }
        catch { /* no browser available — nothing sensible to do */ }
    }
}

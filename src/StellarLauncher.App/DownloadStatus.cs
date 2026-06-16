using StellarLauncher.Core.Services;

namespace StellarLauncher.App;

/// <summary>Formats a <see cref="DownloadProgress"/> into a human status line, e.g.
/// "downloading launcher… 42%  (34.1 / 81.0 MB)" or "downloading… 34.1 MB" when the size is unknown.</summary>
internal static class DownloadStatus
{
    public static string Line(string verb, DownloadProgress p) =>
        p.Fraction is { } f
            ? $"{verb} {f * 100:0}%  ({Mb(p.BytesRead)} / {Mb(p.TotalBytes!.Value)} MB)"
            : $"{verb} {Mb(p.BytesRead)} MB";

    private static string Mb(long bytes) => (bytes / 1_048_576.0).ToString("0.0");
}

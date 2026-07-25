using System;
using System.Text.RegularExpressions;

namespace StellarLauncher.Core.Services;

// Extracts a YouTube video id from the common link forms so the launcher can show the
// thumbnail (img.youtube.com) and open the watch page in the system browser. The launcher
// never embeds a web view — this is deliberately just id/thumbnail plumbing.
public static partial class YouTubeUrl
{
    [GeneratedRegex(@"^(?:https?://)?(?:www\.|m\.)?(?:youtube\.com/(?:watch\?(?:[^#]*&)?v=|shorts/|embed/|live/)|youtu\.be/)([A-Za-z0-9_-]{5,20})",
        RegexOptions.IgnoreCase)]
    private static partial Regex VideoIdRx();

    public static string? TryGetVideoId(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var m = VideoIdRx().Match(url.Trim());
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string ThumbnailUrl(string videoId) => $"https://img.youtube.com/vi/{videoId}/hqdefault.jpg";

    public static string WatchUrl(string videoId) => $"https://www.youtube.com/watch?v={videoId}";
}

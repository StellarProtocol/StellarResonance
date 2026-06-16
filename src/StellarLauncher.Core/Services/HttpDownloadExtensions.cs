using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace StellarLauncher.Core.Services;

/// <summary>Bytes transferred so far, and the total when the server sent Content-Length.</summary>
public readonly record struct DownloadProgress(long BytesRead, long? TotalBytes)
{
    /// <summary>0..1 when the total size is known; null for chunked/unknown-length responses.</summary>
    public double? Fraction =>
        TotalBytes is > 0 ? Math.Clamp((double)BytesRead / TotalBytes.Value, 0, 1) : null;
}

public static class HttpDownloadExtensions
{
    /// <summary>
    /// Streams <paramref name="url"/> into <paramref name="destination"/>, reporting progress as bytes
    /// arrive. Uses ResponseHeadersRead so the body is read incrementally rather than buffered up front —
    /// that's what lets the UI show a live percentage instead of freezing on "downloading…".
    /// </summary>
    public static async Task DownloadToAsync(this HttpClient http, Uri url, Stream destination,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            progress?.Report(new DownloadProgress(read, total));
        }
        progress?.Report(new DownloadProgress(read, total ?? read)); // final 100% tick
    }
}

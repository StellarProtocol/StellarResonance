using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StellarLauncher.Core.Services;

/// <summary>
/// Installs/updates DXVK-NVAPI (jp7677/dxvk-nvapi) into a Wine/Proton prefix: pulls the latest GitHub
/// release tarball, drops x64/nvapi64.dll into the prefix's system32 and x32/nvapi.dll into syswow64,
/// and records the installed tag so repeat launches skip when already current. The launcher adds the
/// matching nvapi DLL overrides at launch (see GameLauncher).
/// </summary>
public sealed class DxvkNvapiInstaller : IDxvkNvapiInstaller
{
    private const string LatestApi = "https://api.github.com/repos/jp7677/dxvk-nvapi/releases/latest";
    private const string Marker = ".dxvk-nvapi-version";
    private readonly HttpClient _http;

    public DxvkNvapiInstaller(HttpClient http) => _http = http;

    public async Task<string> EnsureAsync(string winePrefix, CancellationToken ct = default)
    {
        var (tag, tarUrl) = await LatestReleaseAsync(ct);

        var markerPath = Path.Combine(winePrefix, Marker);
        if (File.Exists(markerPath) && File.ReadAllText(markerPath).Trim() == tag)
            return $"DXVK-NVAPI {tag} (up to date)";

        // GitHub release tarball layout: x64/nvapi64.dll, x32/nvapi.dll
        await using var net = await _http.GetStreamAsync(tarUrl, ct);
        await using var gz = new GZipStream(net, CompressionMode.Decompress);
        using var tar = new TarReader(gz);

        var sys32 = Path.Combine(winePrefix, "drive_c", "windows", "system32");
        var syswow64 = Path.Combine(winePrefix, "drive_c", "windows", "syswow64");
        Directory.CreateDirectory(sys32);
        Directory.CreateDirectory(syswow64);

        var wrote64 = false;
        var wrote32 = false;
        for (TarEntry? e; (e = tar.GetNextEntry()) is not null;)
        {
            ct.ThrowIfCancellationRequested();
            var name = e.Name.Replace('\\', '/');
            if (name.EndsWith("x64/nvapi64.dll", StringComparison.OrdinalIgnoreCase))
            {
                await ExtractTo(e, Path.Combine(sys32, "nvapi64.dll"), ct);
                wrote64 = true;
            }
            else if (name.EndsWith("x32/nvapi.dll", StringComparison.OrdinalIgnoreCase))
            {
                await ExtractTo(e, Path.Combine(syswow64, "nvapi.dll"), ct);
                wrote32 = true;
            }
        }

        if (!wrote64)
            throw new InvalidOperationException("DXVK-NVAPI tarball missing x64/nvapi64.dll");

        File.WriteAllText(markerPath, tag);
        return wrote32 ? $"DXVK-NVAPI {tag} installed" : $"DXVK-NVAPI {tag} installed (64-bit only)";
    }

    private async Task<(string tag, string tarUrl)> LatestReleaseAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, LatestApi);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("StellarLauncher", "1.0"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString()
                  ?? throw new InvalidOperationException("no tag_name in release");
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                return (tag, asset.GetProperty("browser_download_url").GetString()!);
        }
        throw new InvalidOperationException("no .tar.gz asset in the latest DXVK-NVAPI release");
    }

    private static async Task ExtractTo(TarEntry entry, string dest, CancellationToken ct)
    {
        await using var outStream = File.Create(dest);
        if (entry.DataStream is { } data) await data.CopyToAsync(outStream, ct);
    }
}

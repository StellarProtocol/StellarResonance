using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Services;

public sealed class LauncherUpdateService : ILauncherUpdateService
{
    private readonly HttpClient _http;
    public LauncherUpdateService(HttpClient http) => _http = http;

    public async Task<LauncherManifest> FetchAsync(Uri manifestUrl, CancellationToken ct = default)
        => await _http.GetFromJsonAsync<LauncherManifest>(manifestUrl, LauncherManifest.JsonOptions, ct)
           ?? throw new InvalidOperationException("empty launcher manifest");
}

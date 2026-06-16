using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Services;

public sealed class PluginRegistryService : IPluginRegistryService
{
    private readonly HttpClient _http;
    public PluginRegistryService(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<PluginEntry>> FetchAllAsync(
        IEnumerable<Uri> registryUrls, CancellationToken ct = default)
    {
        var byId = new Dictionary<string, PluginEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in registryUrls)
        {
            var registry = await FetchOneAsync(url, ct);   // retries transient failures; null on give-up
            if (registry?.Plugins is null) continue;
            foreach (var plugin in registry.Plugins)
            {
                if (plugin is null || string.IsNullOrWhiteSpace(plugin.Id)) continue;     // guard malformed entries
                if (plugin.Versions is null || plugin.Versions.Count == 0) continue;      // skip legacy/empty
                byId[plugin.Id] = plugin;                                                 // later registries override
            }
        }
        return byId.Values.ToList();
    }

    // Fetch one registry, retrying a couple of times so a transient network blip at startup
    // doesn't drop the whole catalog. Returns null if it can't be loaded (unreachable/invalid).
    private async Task<PluginRegistry?> FetchOneAsync(Uri url, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return await _http.GetFromJsonAsync<PluginRegistry>(url, PluginRegistry.JsonOptions, ct); }
            catch { /* retry below */ }
            try { await Task.Delay(250, ct); } catch { return null; }
        }
        return null;
    }
}

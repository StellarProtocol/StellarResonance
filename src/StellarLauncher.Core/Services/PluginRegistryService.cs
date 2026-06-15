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
            try
            {
                var registry = await _http.GetFromJsonAsync<PluginRegistry>(url, PluginRegistry.JsonOptions, ct);
                if (registry?.Plugins is null) continue;
                foreach (var plugin in registry.Plugins) byId[plugin.Id] = plugin;  // later registries override
            }
            catch { /* unreachable or invalid registry — skip it */ }
        }
        return byId.Values.ToList();
    }
}

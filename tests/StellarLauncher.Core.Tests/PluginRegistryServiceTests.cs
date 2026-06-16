using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Services;
using Xunit;

public class PluginRegistryServiceTests
{
    private sealed class MapHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string?> _map;   // url -> body, null body => 404
        public MapHandler(Dictionary<string, string?> map) => _map = map;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
        {
            var url = r.RequestUri!.ToString();
            if (_map.TryGetValue(url, out var body) && body is not null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private const string Curated = """
    { "plugins": [
      { "id":"combatmeter","name":"CombatMeter","description":"DPS meter","author":"Stellar",
        "versions":[ {"version":"1.0.0","dllUrl":"https://minio.revette.io/stellar/plugins/combatmeter-1.0.0.dll","sha256":"aaa","minModSystemVersion":"1.0.0"} ] },
      { "id":"playerhud","name":"PlayerHUD","description":"HUD","author":"Stellar",
        "versions":[ {"version":"1.0.0","dllUrl":"https://minio.revette.io/stellar/plugins/playerhud-1.0.0.dll","sha256":"bbb","minModSystemVersion":"1.0.0"} ] }
    ] }
    """;

    private const string ThirdParty = """
    { "plugins": [
      { "id":"combatmeter","name":"CombatMeter PRO","description":"override","author":"someone",
        "versions":[ {"version":"2.0.0","dllUrl":"https://example.com/cm-2.0.0.dll","sha256":"ccc","minModSystemVersion":"1.2.0"} ] }
    ] }
    """;

    [Fact]
    public async Task Merges_registries_dedupes_by_id_later_wins_and_skips_unreachable()
    {
        var handler = new MapHandler(new()
        {
            ["https://minio.revette.io/stellar/plugins.json"] = Curated,
            ["https://example.com/repo.json"] = ThirdParty,
            ["https://dead.example/repo.json"] = null,   // 404 → skipped, no throw
        });
        var svc = new PluginRegistryService(new HttpClient(handler));

        var plugins = await svc.FetchAllAsync(new[]
        {
            new Uri("https://minio.revette.io/stellar/plugins.json"),
            new Uri("https://example.com/repo.json"),
            new Uri("https://dead.example/repo.json"),
        });

        var byId = plugins.ToDictionary(p => p.Id);
        Assert.Equal(2, plugins.Count);                                  // combatmeter (overridden) + playerhud
        Assert.Equal("2.0.0", byId["combatmeter"].Versions[0].Version);  // 3rd-party overrode curated
        Assert.Equal("CombatMeter PRO", byId["combatmeter"].Name);
        Assert.Equal("1.2.0", byId["combatmeter"].Versions[0].MinModSystemVersion);
        Assert.True(byId.ContainsKey("playerhud"));
    }

    [Fact]
    public async Task Skips_entries_with_no_versions()
    {
        // A legacy/malformed entry with no versions[] must be ignored, not crash the list.
        const string mixed = """
        { "plugins": [
          { "id":"legacy","name":"Legacy","description":"old flat shape","author":"x" },
          { "id":"good","name":"Good","description":"ok","author":"x",
            "versions":[ {"version":"1.0.0","dllUrl":"https://e/g-1.0.0.dll","sha256":"d","minModSystemVersion":"1.0.0"} ] }
        ] }
        """;
        var svc = new PluginRegistryService(new HttpClient(new MapHandler(new()
        {
            ["https://minio.revette.io/stellar/plugins.json"] = mixed,
        })));

        var plugins = await svc.FetchAllAsync(new[] { new Uri("https://minio.revette.io/stellar/plugins.json") });

        Assert.Single(plugins);
        Assert.Equal("good", plugins[0].Id);
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Services;

public interface IPluginRegistryService
{
    Task<IReadOnlyList<PluginEntry>> FetchAllAsync(IEnumerable<Uri> registryUrls, CancellationToken ct = default);
}

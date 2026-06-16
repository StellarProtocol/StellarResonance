// src/StellarLauncher.Core/Services/IVersionService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Services;

public interface IVersionService
{
    Task<FrameworkManifest> FetchAsync(Uri manifestUrl, CancellationToken ct = default);
}

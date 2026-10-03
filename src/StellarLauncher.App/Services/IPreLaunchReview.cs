using System;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Clients;

namespace StellarLauncher.App.Services;

public interface IPreLaunchReview
{
    /// <returns>false when the user cancelled the launch.</returns>
    Task<bool> ReviewAsync(ClientProfile client, CancellationToken ct);

    /// <summary>Same as <see cref="ReviewAsync(ClientProfile, CancellationToken)"/>, reporting what it is
    /// doing (e.g. "Preparing Photo Studio: …") through <paramref name="status"/> — the client's state line.
    /// A null report clears it.</summary>
    Task<bool> ReviewAsync(ClientProfile client, Action<string?> status, CancellationToken ct) => ReviewAsync(client, ct);
}

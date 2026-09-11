using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Clients;

namespace StellarLauncher.App.Services;

public interface IPreLaunchReview
{
    /// <returns>false when the user cancelled the launch.</returns>
    Task<bool> ReviewAsync(ClientProfile client, CancellationToken ct);
}

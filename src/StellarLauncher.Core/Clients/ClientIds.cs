using System;

namespace StellarLauncher.Core.Clients;

public static class ClientIds
{
    /// <summary>8 lowercase hex chars — stable for the life of the profile, never shown to users.</summary>
    public static string New() => Guid.NewGuid().ToString("N")[..8];
}

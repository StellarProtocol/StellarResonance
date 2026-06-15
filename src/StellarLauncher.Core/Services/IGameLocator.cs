// src/StellarLauncher.Core/Services/IGameLocator.cs
namespace StellarLauncher.Core.Services;

public interface IGameLocator
{
    /// <summary>Newest existing <c>release_*/game_mini</c> under <paramref name="gameRoot"/>, or null.</summary>
    string? FindGameMini(string gameRoot);
}

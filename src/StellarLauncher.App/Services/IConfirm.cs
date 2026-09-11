using System.Threading.Tasks;

namespace StellarLauncher.App.Services;

/// <summary>A yes/no question to the user (a modal dialog in the app; an auto-answer in tests).</summary>
public interface IConfirm
{
    Task<bool> AskAsync(string title, string body, string okLabel);
}

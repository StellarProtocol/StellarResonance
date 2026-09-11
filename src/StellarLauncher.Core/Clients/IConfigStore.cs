namespace StellarLauncher.Core.Clients;

public interface IConfigStore
{
    LauncherConfig Load();
    void Save(LauncherConfig cfg);
    string SettingsPath { get; }
}

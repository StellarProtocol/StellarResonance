using System.Reflection;

namespace StellarLauncher.App.Services;

public static class AppInfo
{
    // Stamped at publish time via -p:Version (release.yml). Local dev builds report 0.0.0-dev.
    public static readonly string LauncherVersion =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+', 2)[0] ?? "0.0.0";

    public static string LauncherVersionLabel => $"Launcher v{LauncherVersion}";
}

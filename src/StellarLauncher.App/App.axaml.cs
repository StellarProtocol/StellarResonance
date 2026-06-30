using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.IO.Abstractions;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using StellarLauncher.App.ViewModels;
using StellarLauncher.App.Views;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var fs = new FileSystem();
        var platform = new PlatformInfo();
        var http = new HttpClient();

        var settings = new SettingsStore(fs, platform);
        var locator = new GameLocator(fs);
        var doorstop = new DoorstopToggle(fs);
        var installer = new Installer(fs);
        var launcher = new GameLauncher(platform);
        var version = new VersionService(http);
        var launcherUpdates = new LauncherUpdateService(http);   // reuse the existing HttpClient
        var selfUpdater = new LauncherSelfUpdater(fs);
        if (Environment.ProcessPath is { } procPath)
            selfUpdater.CleanupStaleUpdate(Path.GetDirectoryName(procPath)!, Path.GetFileName(procPath));
        var detector = new GameDetector(fs, locator,
            () => BuildSearchRoots(fs, platform),
            () => BuildRunnerCandidates(fs, platform),
            () => BuildUmuCandidates(fs, platform));

        var pluginRegistry = new PluginRegistryService(http);
        var pluginInstaller = new PluginInstaller(fs);
        var pluginsVm = new PluginsViewModel(pluginRegistry, pluginInstaller, installer, settings, http);

        var dxvkNvapi = new DxvkNvapiInstaller(http);
        var bepinex = new BepInExConfig(fs);
        var interop = new InteropWatch(fs);

        // Lazily captured — mainWindow is set before the user can ever click Launch.
        MainWindow? mainWindow = null;
        Task<ViewModels.UpdateLaunchChoice> UpdatePrompt(string current, string latest) =>
            new Views.UpdatePromptDialog(current, latest).ShowDialog<ViewModels.UpdateLaunchChoice>(mainWindow!);

        var home = new HomeViewModel(settings, locator, doorstop, installer, launcher, version, platform, launcherUpdates, detector, selfUpdater, dxvkNvapi, bepinex, interop, UpdatePrompt);
        var setVm = new SettingsViewModel(settings, locator, detector, platform);
        var main = new MainWindowViewModel(home, setVm, pluginsVm);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            mainWindow = new MainWindow { DataContext = main };
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Candidate locations the game install (a Wine prefix on Linux, a drive on Windows) may live in.
    private static IReadOnlyList<string> BuildSearchRoots(IFileSystem fs, IPlatformInfo platform)
    {
        var roots = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        void AddChildren(string dir)
        {
            // Never let one unreadable/flaky dir (permission-denied drive root, disconnected mount) abort detection.
            try { if (fs.Directory.Exists(dir)) roots.AddRange(fs.Directory.GetDirectories(dir)); }
            catch { /* skip this dir */ }
        }

        if (platform.IsWindows)
        {
            // Scan every ready fixed/removable drive — the install may live on any drive
            // (e.g. E:\Star\StarLauncher\…), not just C:/D:. For each, probe the root plus the
            // usual install parents so GameDetector's Star\StarLauncher\game suffix can resolve.
            foreach (var drive in EnumerateWindowsDriveRoots(fs))
            {
                roots.Add(drive);
                // Immediate subfolders of the drive — the JP StarASIA client installs to an arbitrary
                // top-level folder (e.g. E:\bpsr\StarLauncher\game\…, no "Star" parent), so adding each
                // drive child as a root lets GameDetector's StarLauncher\game suffix resolve it.
                AddChildren(drive);
                roots.Add(fs.Path.Combine(drive, "Program Files"));
                roots.Add(fs.Path.Combine(drive, "Program Files (x86)"));
                roots.Add(fs.Path.Combine(drive, "Games"));
            }
            // Exact install path straight from the official launcher's uninstall registry entry —
            // covers installs in arbitrary subfolders the drive scan above wouldn't reach.
            roots.AddRange(ReadWindowsRegistryInstallRoots(fs));
        }
        else
        {
            AddChildren("/opt/game");                                          // common manual prefixes
            AddChildren(fs.Path.Combine(home, "Games", "Heroic", "Prefixes")); // Heroic
            AddChildren(fs.Path.Combine(home, "Games"));                       // Lutris / others
            foreach (var steam in new[]
            {
                fs.Path.Combine(home, ".steam", "steam"),
                fs.Path.Combine(home, ".local", "share", "Steam"),
            })
            {
                var compat = fs.Path.Combine(steam, "steamapps", "compatdata");
                if (fs.Directory.Exists(compat))
                    foreach (var app in fs.Directory.GetDirectories(compat))
                        roots.Add(fs.Path.Combine(app, "pfx"));                // Steam Proton
            }
            roots.Add(home);
        }
        return roots;
    }

    // Root directories of every ready fixed/removable drive (C:\, D:\, E:\, …). Windows only.
    private static IEnumerable<string> EnumerateWindowsDriveRoots(IFileSystem fs)
    {
        IDriveInfo[] drives;
        try { drives = fs.DriveInfo.GetDrives(); }
        catch { yield break; }   // never let a flaky drive abort detection

        foreach (var d in drives)
        {
            bool ready;
            try { ready = d.IsReady && (d.DriveType == System.IO.DriveType.Fixed
                                        || d.DriveType == System.IO.DriveType.Removable); }
            catch { continue; }
            if (ready) yield return d.Name;   // e.g. "E:\"
        }
    }

    // Install locations from the official launcher's "Uninstall" registry entries, derived back to
    // the scan root so GameDetector's Star\StarLauncher\game suffix resolves. Windows only.
    private static IReadOnlyList<string> ReadWindowsRegistryInstallRoots(IFileSystem fs)
    {
        var roots = new List<string>();
        if (!OperatingSystem.IsWindows()) return roots;
        try
        {
            var hives = new (Microsoft.Win32.RegistryKey Hive, string Sub)[]
            {
                (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Microsoft.Win32.Registry.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            };
            foreach (var (hive, sub) in hives)
            {
                using var uninstall = hive.OpenSubKey(sub);
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var entry = uninstall.OpenSubKey(name);
                    var loc = (entry?.GetValue("InstallLocation") as string)?.Trim();
                    if (string.IsNullOrEmpty(loc)) continue;
                    // InstallLocation points at the StarLauncher folder itself. The detector's
                    // LauncherSuffix re-appends StarLauncher\game, so hand it the parent dir —
                    // matching just the StarLauncher tail keeps this robust to whatever sits above.
                    loc = loc.TrimEnd('\\', '/');
                    if (!loc.EndsWith("StarLauncher", StringComparison.OrdinalIgnoreCase)) continue;
                    var parent = fs.Path.GetDirectoryName(loc);   // dir containing StarLauncher\
                    if (!string.IsNullOrEmpty(parent)) roots.Add(parent!);
                }
            }
        }
        catch { /* registry unavailable or access denied — fall back to the drive scan */ }
        return roots;
    }

    // Wine/Proton runner binaries in priority order (newest GE-Proton first), Linux only.
    private static IReadOnlyList<string> BuildRunnerCandidates(IFileSystem fs, IPlatformInfo platform)
    {
        if (platform.IsWindows) return System.Array.Empty<string>();

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var list = new List<string>();

        void AddProtons(string toolsDir)
        {
            if (!fs.Directory.Exists(toolsDir)) return;
            var dirs = fs.Directory.GetDirectories(toolsDir);
            Array.Sort(dirs);
            Array.Reverse(dirs);   // newest GE-ProtonX-YY first (lexical desc is a good-enough heuristic)
            foreach (var d in dirs) list.Add(fs.Path.Combine(d, "proton"));
        }

        AddProtons(fs.Path.Combine(home, ".config", "heroic", "tools", "proton"));        // Heroic
        AddProtons(fs.Path.Combine(home, ".steam", "root", "compatibilitytools.d"));      // Steam custom
        AddProtons(fs.Path.Combine(home, ".local", "share", "Steam", "compatibilitytools.d"));
        AddProtons(fs.Path.Combine(home, ".steam", "steam", "steamapps", "common"));      // Steam official Proton*
        list.Add("/usr/bin/wine");
        list.Add("/usr/local/bin/wine");
        return list;
    }

    // umu-run launcher (preferred for Proton), Linux only.
    private static IReadOnlyList<string> BuildUmuCandidates(IFileSystem fs, IPlatformInfo platform)
    {
        if (platform.IsWindows) return System.Array.Empty<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[]
        {
            "/usr/bin/umu-run",
            "/usr/local/bin/umu-run",
            fs.Path.Combine(home, ".local", "bin", "umu-run"),
            fs.Path.Combine(home, ".config", "heroic", "tools", "runtimes", "umu", "umu-run"), // Heroic bundle
        };
    }
}

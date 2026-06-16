using System;
using System.Collections.Generic;
using System.IO;
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

        var home = new HomeViewModel(settings, locator, doorstop, installer, launcher, version, platform, launcherUpdates, detector, selfUpdater);
        var setVm = new SettingsViewModel(settings, locator, detector);
        var main = new MainWindowViewModel(home, setVm, pluginsVm);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow { DataContext = main };

        base.OnFrameworkInitializationCompleted();
    }

    // Candidate locations the game install (a Wine prefix on Linux, a drive on Windows) may live in.
    private static IReadOnlyList<string> BuildSearchRoots(IFileSystem fs, IPlatformInfo platform)
    {
        var roots = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        void AddChildren(string dir)
        {
            if (fs.Directory.Exists(dir)) roots.AddRange(fs.Directory.GetDirectories(dir));
        }

        if (platform.IsWindows)
        {
            roots.Add(@"C:\");
            roots.Add(@"C:\Program Files");
            roots.Add(@"C:\Program Files (x86)");
            roots.Add(@"D:\");
            roots.Add(@"D:\Games");
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

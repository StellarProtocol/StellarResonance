using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Net.Http;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels;
using StellarLauncher.App.ViewModels.AddClient;
using StellarLauncher.App.ViewModels.Dashboard;
using StellarLauncher.App.ViewModels.Shell;
using StellarLauncher.App.ViewModels.Workspace;
using StellarLauncher.App.Views;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Launch;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>Composition root: every service is built once here and handed to the shell's page factories.</summary>
    public override void OnFrameworkInitializationCompleted()
    {
        var fs = new FileSystem();
        var platform = new PlatformInfo();
        var http = new HttpClient();
        Views.MarkdownView.Http = http;   // guide images share the app-wide client

        var store = new ConfigStore(fs, platform);
        var locator = new GameLocator(fs);
        var doorstop = new DoorstopToggle(fs);
        var installer = new Installer(fs);
        var pluginInstaller = new PluginInstaller(fs);
        var detector = new GameDetector(fs, locator,
            () => BuildSearchRoots(fs, platform), () => BuildRunnerCandidates(fs, platform), () => BuildUmuCandidates(fs, platform));
        var selfUpdater = new LauncherSelfUpdater(fs);
        if (Environment.ProcessPath is { } procPath)
            selfUpdater.CleanupStaleUpdate(Path.GetDirectoryName(procPath)!, Path.GetFileName(procPath));

        // Lazily captured — both exist before the user can ever click anything.
        MainWindow? mainWindow = null;
        ShellViewModel? shell = null;

        var deps = new PluginInstallDeps(installer, pluginInstaller, http);
        var inventory = new ClientInventory(fs, installer, pluginInstaller, doorstop);
        var registry = new RegistryCache(new PluginRegistryService(http), () => shell!.Config);
        var versions = new VersionService(http);
        var manifests = new FrameworkManifests(versions);
        var confirm = new ConfirmDialog(() => mainWindow);
        var review = new PreLaunchReviewService(registry, inventory, versions, deps, vm => PreLaunchReviewDialog.ShowFor(vm, mainWindow!));

        var env = new LaunchEnvironment(fs, platform, detector);
        var orchestrator = new LaunchOrchestrator(new GameLauncher(platform), new SystemProcessFactory(), new BepInExConfig(fs),
            new DxvkNvapiInstaller(http), new InteropMonitor(new InteropWatch(fs), env), env);
        IRunningProcessScanner scanner = platform.IsWindows ? new WindowsProcessScanner() : new ProcFsScanner(fs);
        var sessions = new ClientSessions(store, orchestrator, scanner, new SystemProcessFactory(),
            () => DateTimeOffset.UtcNow, a => Dispatcher.UIThread.Post(a));

        var core = new DashboardServices(inventory, registry, manifests, review, deps, new ClientCandidates(detector, platform));
        var wsServices = new WorkspaceServices(core, doorstop, fs, platform, detector, locator, confirm, UiTimer);
        var tabs = new WorkspaceTabFactories(w => new OverviewViewModel(w), w => new ClientPluginsViewModel(w),
            w => new ClientSettingsViewModel(w), w => new LogsViewModel(w));
        var launcherSvc = new LauncherServices(new LauncherUpdateService(http), selfUpdater, platform, store, http, fs);

        var shellVm = new ShellViewModel(store, sessions, new ShellPages(
            Dashboard: s => new DashboardViewModel(s, core),
            Workspace: (s, c) => new ClientWorkspaceViewModel(s, c, wsServices, tabs),
            AddClient: s => new AddClientViewModel(s, wsServices),
            LauncherSettings: s => new LauncherSettingsViewModel(s, launcherSvc)));
        shell = shellVm;
        // The rail's quick ▶ must work from the very first frame, including a `StartOn == "lastClient"` boot that never
        // shows the Dashboard — so the ONE launch path is wired here, not inside a page constructor.
        shellVm.QuickLaunchHandler = c => sessions.LaunchAsync(c, review, CancellationToken.None);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            mainWindow = new MainWindow { DataContext = shellVm };
            desktop.MainWindow = mainWindow;
            // Regaining focus (after publishing a release from another window, or alt-tabbing back from the
            // game) should always show current state — force a refetch rather than wait out the cache TTL.
            // The TTL still shields rapid in-app navigation (tab/client switches) from hammering the CDN.
            mainWindow.Activated += (_, _) =>
            {
                manifests.Invalidate();
                registry.Invalidate();
                switch (shellVm.Current)
                {
                    case DashboardViewModel d: _ = d.RefreshAsync(); break;
                    case ClientWorkspaceViewModel w: _ = w.RefreshAsync(); break;
                }
            };
        }

        // "Keep the launcher open while clients run" off → minimise once a client reaches Running.
        sessions.SessionChanged += s =>
        {
            if (!shellVm.Config.Launcher.KeepOpen && s.State == SessionState.Running && mainWindow is not null)
                mainWindow.WindowState = WindowState.Minimized;
        };

        shellVm.Start();
        _ = LauncherSettingsViewModel.CheckUpdatesAsync(shellVm, launcherSvc);   // guarded inside; offline = no banner
        base.OnFrameworkInitializationCompleted();
    }

    // The Logs tab's poll: a DispatcherTimer that exists only while the tab is on screen (the view disposes it on detach).
    private static IDisposable UiTimer(TimeSpan period, Action tick)
    {
        var t = new DispatcherTimer { Interval = period };
        t.Tick += (_, _) => tick();
        t.Start();
        return new StopOnDispose(t);
    }

    private sealed class StopOnDispose(DispatcherTimer t) : IDisposable { public void Dispose() => t.Stop(); }

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

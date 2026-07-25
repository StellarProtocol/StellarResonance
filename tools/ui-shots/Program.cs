using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StellarLauncher.App.ViewModels;
using StellarLauncher.App.Views;

namespace StellarLauncher.UiShots;

// Drives the real launcher app on the headless Avalonia platform (Skia rendering, no display
// server) and captures PNGs of Home + Plugins list/detail at normal and small window sizes.
// The app still talks to its configured registries — isolate via XDG_CONFIG_HOME.
internal static class Program
{
    private static string _outDir = "ui-shots-out";

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0) _outDir = args[0];
        Directory.CreateDirectory(_outDir);

        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = Array.Empty<string>(),
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        AppBuilder.Configure<StellarLauncher.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .SetupWithLifetime(lifetime);

        var window = lifetime.MainWindow!;
        window.Show();
        var main = (MainWindowViewModel)window.DataContext!;

        // -------- Home: framework changelog (patch-note wrap fix) --------
        Resize(window, 1100, 720);
        Require(WaitUntil(() => main.Home.Changelog.Count > 0), "framework changelog loaded");
        Pump(600);
        Shot(window, "01-home-1100x720");

        Resize(window, 760, 500);
        Shot(window, "02-home-760x500-small");

        // -------- Plugins list --------
        Resize(window, 1100, 720);
        main.ShowPluginsCommand.Execute(null);
        var plugins = main.Plugins;
        Require(WaitUntil(() => plugins.Plugins.Count >= 5), "plugin registry loaded");
        Pump(400);
        Shot(window, "03-plugins-list");

        // -------- Detail page (CombatMeter — media + guide + changelog) --------
        var meter = plugins.Plugins.FirstOrDefault(p => p.Entry.Id == "combatmeter")
                    ?? plugins.Plugins[0];
        plugins.OpenPluginCommand.Execute(meter);
        WaitUntil(() => meter.GuideMarkdown is not null
                        && meter.Media.Count > 0
                        && meter.Media.Any(m => m.Thumbnail is not null), 20000);
        Pump(1500);   // guide-embedded images load after the markdown renders
        Shot(window, "04-detail-top");

        var scroller = DetailScroller(window);
        if (scroller is not null)
        {
            scroller.Offset = new Vector(0, scroller.Extent.Height * 0.32);
            Pump(300);
            Shot(window, "05-detail-guide");
            scroller.ScrollToEnd();
            Pump(300);
            Shot(window, "06-detail-changelog");
            scroller.ScrollToHome();
            Pump(200);
        }

        // -------- Lightbox --------
        var image = meter.Media.FirstOrDefault(m => m.IsImage && m.Thumbnail is not null);
        if (image is not null)
        {
            image.OpenCommand.Execute(null);
            Require(WaitUntil(() => plugins.IsLightboxOpen), "lightbox opened");
            Pump(300);
            Shot(window, "07-detail-lightbox");
            plugins.CloseLightboxCommand.Execute(null);
            Pump(200);
        }

        // -------- Small-window detail (wrap behaviour under pressure) --------
        Resize(window, 760, 500);
        Shot(window, "08-detail-760x500-small");

        plugins.CloseDetailCommand.Execute(null);
        Pump(200);
        Shot(window, "09-plugins-list-760x500-small");

        lifetime.Shutdown();
        Console.WriteLine($"done → {Path.GetFullPath(_outDir)}");
    }

    private static void Resize(Window w, double width, double height)
    {
        w.Width = width;
        w.Height = height;
        Pump(250);
    }

    private static void Pump(int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 15000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return true;
            Thread.Sleep(20);
        }
        return condition();
    }

    private static void Require(bool ok, string what)
    {
        if (ok) { Console.WriteLine($"ok: {what}"); return; }
        Console.Error.WriteLine($"TIMEOUT waiting for: {what}");
        Environment.Exit(1);
    }

    // The detail page's outer vertical scroller = the visible ScrollViewer with the tallest
    // scrollable extent (gallery/code scrollers are horizontal; the hidden list page doesn't count).
    private static ScrollViewer? DetailScroller(Window w) => w
        .GetVisualDescendants().OfType<ScrollViewer>()
        .Where(s => s.IsEffectivelyVisible && s.Extent.Height > s.Viewport.Height)
        .OrderByDescending(s => s.Extent.Height)
        .FirstOrDefault();

    private static void Shot(Window w, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = w.CaptureRenderedFrame()
                    ?? throw new InvalidOperationException("no rendered frame");
        var path = Path.Combine(_outDir, name + ".png");
        frame.Save(path);
        Console.WriteLine($"shot {name} ({frame.PixelSize.Width}x{frame.PixelSize.Height})");
    }
}

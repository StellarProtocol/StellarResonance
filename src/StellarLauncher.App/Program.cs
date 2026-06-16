using Avalonia;
using Avalonia.Logging;
using System;
using System.Diagnostics;
using System.IO;

namespace StellarLauncher.App;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        var b = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Fixed WM_CLASS so the X11 window maps to stellar-launcher.desktop and the
            // taskbar/dock picks up the installed icon (see packaging/linux). No-op off X11.
            .With(new X11PlatformOptions { WmClass = "stellar-launcher" })
            .WithInterFont();
#if DEBUG
        b = b.WithDeveloperTools();
#endif
        // Release builds are silent for perf (no console — WinExe — and no logging). Opt back in for
        // troubleshooting with STELLAR_LAUNCHER_DEBUG=1 or --debug: logs to stellar-launcher.log next
        // to the executable. Debug builds always log.
        if (DebugLoggingEnabled())
        {
            TryAddFileListener();
            b = b.LogToTrace(LogEventLevel.Information);
        }
        return b;
    }

    private static bool DebugLoggingEnabled()
    {
#if DEBUG
        return true;
#else
        var v = Environment.GetEnvironmentVariable("STELLAR_LAUNCHER_DEBUG");
        if (v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var a in Environment.GetCommandLineArgs())
            if (a is "--debug" or "-d") return true;
        return false;
#endif
    }

    private static void TryAddFileListener()
    {
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            Trace.Listeners.Add(new TextWriterTraceListener(Path.Combine(dir, "stellar-launcher.log")));
            Trace.AutoFlush = true;
        }
        catch { /* logging is best-effort; never block startup */ }
    }
}

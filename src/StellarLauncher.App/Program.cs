using Avalonia;
using System;

namespace StellarLauncher.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Fixed WM_CLASS so the X11 window maps to stellar-launcher.desktop and the
            // taskbar/dock picks up the installed icon (see packaging/linux). No-op off X11.
            .With(new X11PlatformOptions { WmClass = "stellar-launcher" })
#if DEBUG
            .WithDeveloperTools()
            .LogToTrace()   // Avalonia diagnostic logging — debug builds only; Release stays silent
#endif
            .WithInterFont();
}

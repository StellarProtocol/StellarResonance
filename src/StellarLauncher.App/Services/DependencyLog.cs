using System;
using System.Diagnostics;
using System.IO;

namespace StellarLauncher.App.Services;

/// <summary>Final review M-f: the one always-on log line for a dependency park / unpark / ensure FAILURE.
/// Every line goes to <see cref="Trace"/> (the launcher log while debug logging is on — see Program.cs); a
/// Release build without debug logging has no such listener, so <see cref="AlwaysOnFile"/> (set by Program in
/// exactly that case) receives these failure lines too — and nothing else.</summary>
public static class DependencyLog
{
    /// <summary>stellar-launcher.log when debug logging is OFF; null otherwise (Trace already writes it then).</summary>
    public static string? AlwaysOnFile { get; set; }

    public static void Failure(string where, string message) => WriteLine($"[deps] {where}: {message}");

    /// <summary>R-2: the owner's "tell me" announcement — a non-failure dependency event (currently just
    /// "Added &lt;dependency&gt; for &lt;plugin&gt;") — gets the same always-on line a failure does, so it's
    /// on record even if the player never notices the tile's sticky notice.</summary>
    public static void Notice(string where, string message) => WriteLine($"[deps] {where}: {message}");

    private static void WriteLine(string line)
    {
        Trace.WriteLine(line);
        if (AlwaysOnFile is not { } path) return;
        try { File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}"); }
        catch { /* best effort — logging must never break a launch */ }
    }
}

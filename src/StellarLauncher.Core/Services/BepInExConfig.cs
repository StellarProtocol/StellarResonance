using System.IO.Abstractions;

namespace StellarLauncher.Core.Services;

/// <summary>
/// Tunes the game's BepInEx.cfg for prod vs debug on launch — mirroring the legacy install-stellar.sh
/// prod/test modes. Prod (default) is the fast path: no console window (under Wine each log line is a
/// GDI redraw), buffered disk logging. Debug turns the console + instant-flush back on for crash
/// triage. UnityLogListening is always off — it's a per-frame perf cost with no prod value.
/// </summary>
public interface IBepInExConfig
{
    void ApplyMode(string gameMiniDir, bool debug);
}

public sealed class BepInExConfig : IBepInExConfig
{
    private readonly IFileSystem _fs;
    public BepInExConfig(IFileSystem fs) => _fs = fs;

    public void ApplyMode(string gameMiniDir, bool debug)
    {
        var path = _fs.Path.Combine(gameMiniDir, "BepInEx", "config", "BepInEx.cfg");
        if (!_fs.File.Exists(path)) return;   // generated on first BepInEx run — nothing to tune yet

        var consoleAndFlush = debug ? "true" : "false";
        var lines = _fs.File.ReadAllLines(path);
        string? section = null;
        var changed = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith('['))
            {
                section = t.Trim();
                continue;
            }
            if (t.StartsWith("UnityLogListening")) { lines[i] = SetValue(lines[i], "false"); changed = true; }
            else if (t.StartsWith("InstantFlushing")) { lines[i] = SetValue(lines[i], consoleAndFlush); changed = true; }
            // Both [Logging.Console] and [Logging.Disk] have an "Enabled" key — only touch the Console one.
            else if (t.StartsWith("Enabled") && section == "[Logging.Console]") { lines[i] = SetValue(lines[i], consoleAndFlush); changed = true; }
        }
        if (changed) _fs.File.WriteAllLines(path, lines);
    }

    // Replace the value after '=' while preserving the key name + spacing.
    private static string SetValue(string line, string value)
    {
        var eq = line.IndexOf('=');
        return eq < 0 ? line : string.Concat(line.AsSpan(0, eq + 1), " ", value);
    }
}

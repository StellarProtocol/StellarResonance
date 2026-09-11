using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;

namespace StellarLauncher.Core.Logs;

public enum LogLevel { Debug, Info, Message, Warning, Error, Fatal, Unknown }

public sealed record LogLine(LogLevel Level, string Source, string Text, string Raw);

/// <summary>Last N lines of a BepInEx-style log, parsed. Callers poll on a bounded timer only while visible.</summary>
public static class LogTail
{
    private static readonly Regex Prefix = new(@"^\[(?<lvl>[A-Za-z]+)\s*:\s*(?<src>[^\]]+?)\s*\]\s?(?<msg>.*)$", RegexOptions.Compiled);

    public static IReadOnlyList<LogLine> ReadLast(IFileSystem fs, string path, int maxLines)
    {
        if (!fs.File.Exists(path)) return Array.Empty<LogLine>();
        string[] lines;
        try { lines = fs.File.ReadAllLines(path); }
        catch { return Array.Empty<LogLine>(); }
        return lines.Where(l => l.Length > 0).TakeLast(Math.Max(0, maxLines)).Select(Parse).ToList();
    }

    public static LogLine Parse(string raw)
    {
        var m = Prefix.Match(raw);
        if (!m.Success) return new LogLine(LogLevel.Unknown, "", raw, raw);
        var level = Enum.TryParse<LogLevel>(m.Groups["lvl"].Value, ignoreCase: true, out var l) ? l : LogLevel.Unknown;
        return new LogLine(level, m.Groups["src"].Value, m.Groups["msg"].Value, raw);
    }
}

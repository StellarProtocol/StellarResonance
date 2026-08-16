using System.Collections.Generic;
using System.Text;

namespace StellarLauncher.Core.Services;

/// <summary>Quote-aware splitting of a command-line string into argv tokens.</summary>
public static class CommandLine
{
    public static IReadOnlyList<string> Split(string? s)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(s)) return result;

        var sb = new StringBuilder();
        var quote = '\0';
        var has = false;

        foreach (var c in s)
        {
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else sb.Append(c);
            }
            else if (c is '"' or '\'') { quote = c; has = true; }
            else if (char.IsWhiteSpace(c))
            {
                if (has) { result.Add(sb.ToString()); sb.Clear(); has = false; }
            }
            else { sb.Append(c); has = true; }
        }

        if (has) result.Add(sb.ToString());
        return result;
    }
}

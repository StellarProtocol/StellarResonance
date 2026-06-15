// src/StellarLauncher.Core/Services/DoorstopToggle.cs
using System;
using System.IO.Abstractions;
using System.Text.RegularExpressions;

namespace StellarLauncher.Core.Services;

public sealed class DoorstopToggle : IDoorstopToggle
{
    private readonly IFileSystem _fs;
    public DoorstopToggle(IFileSystem fs) => _fs = fs;

    public bool IsEnabled(string path)
    {
        var m = Match(_fs.File.ReadAllText(path));
        return m.Success && string.Equals(m.Groups["val"].Value.Trim(), "true",
                                           StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(string path, bool enabled)
    {
        var text = _fs.File.ReadAllText(path);
        var replaced = GeneralEnabled.Replace(text,
            m => $"{m.Groups["pre"].Value}{(enabled ? "true" : "false")}", 1);
        _fs.File.WriteAllText(path, replaced);
    }

    // Matches `enabled = X` that appears after the [General] header and before the next [section].
    private static readonly Regex GeneralEnabled = new(
        @"(?<pre>\[General\][^\[]*?^\s*enabled\s*=\s*)(?<val>\w+)",
        RegexOptions.Multiline | RegexOptions.Singleline);

    private static Match Match(string text) => GeneralEnabled.Match(text);
}

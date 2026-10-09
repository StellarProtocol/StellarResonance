using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Pins the i18n string sweep (launcher i18n Task 3): no view may carry a hard-coded user-visible English string —
/// every Text / Content / Header / ToolTip.Tip / PlaceholderText / OnContent / OffContent / Title / Watermark / Run Text
/// value is a binding or <c>{loc:T key}</c>. The only literals allowed are brand names, paths and technical identifiers
/// (the allowlist below) and values with no letters at all (glyphs like ⧉ 🗑 ·).
/// </summary>
public class AxamlLiteralSweepTests
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "Stellar Launcher",             // brand: window title + rail wordmark
        "https://…/manifest.json",      // URL placeholder
        "/path/to/GE-Proton/proton",    // path placeholder
        "/path/to/prefix",              // path placeholder
        "WINEPREFIX",                   // technical identifier (env var name)
        "Esync", "Fsync",               // Wine feature names
        "NAME", "VALUE",                // environment-variable placeholders (identifier columns)
        "Consolas, DejaVu Sans Mono, Menlo, monospace",   // Theme.axaml MonoFont resource (a font list, not UI text)
    };

    private static readonly Regex Attr = new(
        @"(?<![\w.])(Text|Content|Header|ToolTip\.Tip|PlaceholderText|OnContent|OffContent|Title|Watermark)=""(?<v>[^""]*)""",
        RegexOptions.CultureInvariant);

    [Fact]
    public void No_view_contains_a_hard_coded_user_visible_string()
    {
        var views = Directory.GetFiles(Path.Combine(RepoRoot(), "src", "StellarLauncher.App"), "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.True(views.Count >= 13, $"expected the launcher's views, found {views.Count}");

        var offenders = new List<string>();
        foreach (var file in views)
        {
            var xaml = Regex.Replace(File.ReadAllText(file), "<!--.*?-->", "", RegexOptions.Singleline);
            foreach (Match m in Attr.Matches(xaml))
            {
                var v = m.Groups["v"].Value;
                if (v.StartsWith('{') || !v.Any(char.IsLetter) || Allowed.Contains(v)) continue;
                offenders.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}=\"{v}\"");
            }
            foreach (Match m in ElementText.Matches(xaml).Where(m => !Allowed.Contains(m.Groups["v"].Value.Trim())))
                offenders.Add($"{Path.GetFileName(file)}: element text \"{m.Groups["v"].Value.Trim()}\"");
            foreach (Match m in LetteredFormat.Matches(xaml))
                offenders.Add($"{Path.GetFileName(file)}: StringFormat='{m.Groups["v"].Value}'");
        }
        Assert.True(offenders.Count == 0, "hard-coded UI strings (use {loc:T key}):\n" + string.Join("\n", offenders));
    }

    // The sweep must actually see literals: a planted one is reported (guards against a regex that matches nothing).
    // Text placed as element content (<TextBlock>Hi</TextBlock>, <ToolTip.Tip>Hi</ToolTip.Tip>) — any lettered text node.
    private static readonly Regex ElementText = new(@">(?<v>[^<>{}]*\p{L}[^<>]*)</", RegexOptions.CultureInvariant);

    // A binding StringFormat carrying words; the version prefix 'v{0}' is the one allowed letter.
    private static readonly Regex LetteredFormat = new(@"StringFormat='(?<v>(?!v\{0\}')[^']*\p{L}[^']*)'", RegexOptions.CultureInvariant);

    [Fact]
    public void The_sweep_also_detects_element_text_and_lettered_string_formats()
    {
        Assert.True(ElementText.IsMatch("<TextBlock>Hello</TextBlock>"));
        Assert.True(ElementText.IsMatch("<ToolTip.Tip>Open it</ToolTip.Tip>"));
        Assert.False(ElementText.IsMatch("<TextBlock Text=\"{loc:T a.b}\"/>"));
        Assert.True(LetteredFormat.IsMatch("{Binding N, StringFormat='Installed {0}'}"));
        Assert.False(LetteredFormat.IsMatch("{Binding V, StringFormat='v{0}'}"));
        Assert.False(LetteredFormat.IsMatch("{Binding U, StringFormat='{}{0}↑'}"));
    }

    [Fact]
    public void The_sweep_detects_a_planted_literal()
    {
        const string sample = """<TextBlock Text="Hello there"/><Button Content="{loc:T common.cancel}"/><Run Text="Oops"/>""";
        var found = Attr.Matches(sample).Select(m => m.Groups["v"].Value).Where(v => !v.StartsWith('{')).ToList();
        Assert.Equal(new[] { "Hello there", "Oops" }, found);
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "StellarLauncher.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("launcher repo root not found");
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using StellarLauncher.Core.Model;

namespace StellarLauncher.Core.Services;

// Line-based parser for the guide-markdown subset (see MarkdownBlocks.cs). No HTML pass-through:
// raw HTML stays literal text, so a registry guide can never inject markup into the launcher.
public static partial class MarkdownParser
{
    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]                         private static partial Regex HeadingRx();
    [GeneratedRegex(@"^\s*(-{3,}|\*{3,}|_{3,})\s*$")]              private static partial Regex RuleRx();
    [GeneratedRegex(@"^!\[([^\]]*)\]\(([^)\s]+)\)\s*$")]           private static partial Regex ImageRx();
    [GeneratedRegex(@"^(\s*)([-*+]|\d+[.)])\s+(.*)$")]             private static partial Regex ListItemRx();
    [GeneratedRegex(@"^\[([^\]]+)\]\(([^)\s]+)\)")]                private static partial Regex LinkRx();

    public static IReadOnlyList<MdBlock> Parse(string markdown)
    {
        var blocks = new List<MdBlock>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length;)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { blocks.Add(ReadCodeBlock(lines, ref i)); continue; }
            if (HeadingRx().Match(line) is { Success: true } h)
            {
                blocks.Add(new MdHeading(h.Groups[1].Length, ParseInlines(h.Groups[2].Value.Trim())));
                i++; continue;
            }
            if (RuleRx().IsMatch(line)) { blocks.Add(new MdRule()); i++; continue; }
            if (ImageRx().Match(line) is { Success: true } img)
            {
                blocks.Add(new MdImage(img.Groups[2].Value, img.Groups[1].Value));
                i++; continue;
            }
            if (line.TrimStart().StartsWith('>')) { blocks.Add(ReadQuote(lines, ref i)); continue; }
            if (ListItemRx().IsMatch(line)) { blocks.Add(ReadList(lines, ref i)); continue; }
            blocks.Add(ReadParagraph(lines, ref i));
        }
        return blocks;
    }

    private static MdCodeBlock ReadCodeBlock(string[] lines, ref int i)
    {
        i++;   // skip the opening fence (language tag, if any, is ignored)
        var body = new StringBuilder();
        while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            if (body.Length > 0) body.Append('\n');
            body.Append(lines[i]);
            i++;
        }
        if (i < lines.Length) i++;   // skip the closing fence
        return new MdCodeBlock(body.ToString());
    }

    private static MdQuote ReadQuote(string[] lines, ref int i)
    {
        var text = new StringBuilder();
        while (i < lines.Length && lines[i].TrimStart().StartsWith('>'))
        {
            var t = lines[i].TrimStart().TrimStart('>').Trim();
            if (t.Length > 0)
            {
                if (text.Length > 0) text.Append(' ');
                text.Append(t);
            }
            i++;
        }
        return new MdQuote(ParseInlines(text.ToString()));
    }

    // A line that begins some other block construct (used to stop paragraphs and list items).
    private static bool StartsBlock(string line) =>
        line.TrimStart().StartsWith("```", StringComparison.Ordinal)
        || HeadingRx().IsMatch(line) || RuleRx().IsMatch(line) || ImageRx().IsMatch(line)
        || line.TrimStart().StartsWith('>') || ListItemRx().IsMatch(line);

    private static MdList ReadList(string[] lines, ref int i)
    {
        var items = new List<MdListItem>();
        while (i < lines.Length && ListItemRx().Match(lines[i]) is { Success: true } m)
        {
            var indent = m.Groups[1].Value.Length >= 2 ? 1 : 0;
            var marker = m.Groups[2].Value;
            if (marker is "-" or "*" or "+") marker = indent == 0 ? "•" : "◦";
            else marker = marker.TrimEnd(')', '.') + ".";
            var text = new StringBuilder(m.Groups[3].Value.Trim());
            i++;
            // Lazy continuation: following plain lines (wrapped source) belong to this item.
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !StartsBlock(lines[i]))
            {
                text.Append(' ').Append(lines[i].Trim());
                i++;
            }
            items.Add(new MdListItem(ParseInlines(text.ToString()), indent, marker));
        }
        return new MdList(items);
    }

    private static MdParagraph ReadParagraph(string[] lines, ref int i)
    {
        var text = new StringBuilder();
        while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !StartsBlock(lines[i]))
        {
            if (text.Length > 0) text.Append(' ');
            text.Append(lines[i].Trim());
            i++;
        }
        return new MdParagraph(ParseInlines(text.ToString()));
    }

    // Inline subset: `code`, **bold**, *italic*, [text](url), ![alt](url) (rendered as a link).
    // An unterminated marker is kept as literal text. No nesting inside bold/italic.
    public static IReadOnlyList<MdInline> ParseInlines(string text)
    {
        var result = new List<MdInline>();
        var plain = new StringBuilder();
        void Flush() { if (plain.Length > 0) { result.Add(new MdText(plain.ToString())); plain.Clear(); } }

        for (var i = 0; i < text.Length;)
        {
            var rest = text.AsSpan(i);
            if (rest[0] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i) { Flush(); result.Add(new MdText(text[(i + 1)..end], Code: true)); i = end + 1; continue; }
            }
            else if (rest.StartsWith("**"))
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > i + 1) { Flush(); result.Add(new MdText(text[(i + 2)..end], Bold: true)); i = end + 2; continue; }
            }
            else if (rest[0] == '*')
            {
                var end = text.IndexOf('*', i + 1);
                if (end > i + 1) { Flush(); result.Add(new MdText(text[(i + 1)..end], Italic: true)); i = end + 1; continue; }
            }
            else if (rest[0] == '[' && LinkRx().Match(text[i..]) is { Success: true } link)
            {
                Flush();
                result.Add(new MdLink(link.Groups[1].Value, link.Groups[2].Value));
                i += link.Length; continue;
            }
            else if (rest.StartsWith("![") && LinkRx().Match(text[(i + 1)..]) is { Success: true } inlineImg)
            {
                Flush();
                var alt = inlineImg.Groups[1].Value;
                result.Add(new MdLink(alt.Length > 0 ? alt : "image", inlineImg.Groups[2].Value));
                i += 1 + inlineImg.Length; continue;
            }
            plain.Append(text[i]);
            i++;
        }
        Flush();
        return result;
    }
}

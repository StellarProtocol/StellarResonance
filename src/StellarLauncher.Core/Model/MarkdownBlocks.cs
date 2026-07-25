using System.Collections.Generic;

namespace StellarLauncher.Core.Model;

// Block/inline model produced by MarkdownParser and rendered by the App's MarkdownView.
// Deliberately a small, well-defined subset (plugin guides are curated registry content):
// headings, paragraphs, flat lists, fenced code, quotes, rules, block images, and
// bold / italic / inline-code / link inlines. Anything else renders as literal text.

public abstract record MdInline;
public sealed record MdText(string Text, bool Bold = false, bool Italic = false, bool Code = false) : MdInline;
public sealed record MdLink(string Text, string Url) : MdInline;

public abstract record MdBlock;
public sealed record MdHeading(int Level, IReadOnlyList<MdInline> Inlines) : MdBlock;
public sealed record MdParagraph(IReadOnlyList<MdInline> Inlines) : MdBlock;
public sealed record MdListItem(IReadOnlyList<MdInline> Inlines, int Indent, string Marker);
public sealed record MdList(IReadOnlyList<MdListItem> Items) : MdBlock;
public sealed record MdCodeBlock(string Text) : MdBlock;
public sealed record MdQuote(IReadOnlyList<MdInline> Inlines) : MdBlock;
public sealed record MdRule : MdBlock;
public sealed record MdImage(string Url, string Alt) : MdBlock;

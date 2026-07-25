using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;

namespace StellarLauncher.App.Views;

// Renders the guide-markdown subset (MarkdownParser) as native controls — no web view, no
// third-party markdown package (none supports Avalonia 12). Links open the system browser;
// images load async through the app-wide HttpClient assigned to Http at startup.
public sealed class MarkdownView : ContentControl
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    // URL of the markdown document itself; relative image/link targets resolve against it
    // (GuideUrls), so guides reference "media/shot.png" without knowing their CDN location.
    public static readonly StyledProperty<string?> BaseUrlProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(BaseUrl));

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public string? BaseUrl
    {
        get => GetValue(BaseUrlProperty);
        set => SetValue(BaseUrlProperty, value);
    }

    // App-wide HttpClient for guide images; set once in App.OnFrameworkInitializationCompleted.
    public static HttpClient? Http { get; set; }

    private static readonly IBrush Body = new SolidColorBrush(Color.Parse("#c5cce0"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#9aa3bf"));
    private static readonly IBrush CodeFg = new SolidColorBrush(Color.Parse("#9ec2ff"));
    private static readonly IBrush LinkFg = new SolidColorBrush(Color.Parse("#5b8cff"));
    private static readonly IBrush PanelBg = new SolidColorBrush(Color.Parse("#0d1120"));
    private static readonly IBrush Line = new SolidColorBrush(Color.Parse("#222a44"));
    private static readonly FontFamily Mono = new("Consolas,Menlo,DejaVu Sans Mono,monospace");

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty || change.Property == BaseUrlProperty) Rebuild();
    }

    private void Rebuild()
    {
        var text = Markdown;
        if (string.IsNullOrWhiteSpace(text)) { Content = null; return; }
        var root = new StackPanel { Spacing = 10 };
        foreach (var block in MarkdownParser.Parse(text))
            root.Children.Add(RenderBlock(block, BaseUrl));
        Content = root;
    }

    private static Control RenderBlock(MdBlock block, string? baseUrl) => block switch
    {
        MdHeading h => RenderHeading(h, baseUrl),
        MdParagraph p => RenderText(p.Inlines, 13, Body, baseUrl),
        MdList l => RenderList(l, baseUrl),
        MdCodeBlock c => RenderCode(c),
        MdQuote q => RenderQuote(q, baseUrl),
        MdImage img => RenderImage(img, baseUrl),
        _ => new Border { Height = 1, Background = Line, Margin = new Thickness(0, 4) },   // MdRule
    };

    private static Control RenderHeading(MdHeading h, string? baseUrl)
    {
        var size = h.Level switch { 1 => 19, 2 => 16, 3 => 14, _ => 13 };
        var tb = RenderText(h.Inlines, size, Brushes.White, baseUrl);
        tb.FontWeight = FontWeight.Bold;
        tb.Margin = new Thickness(0, h.Level <= 2 ? 8 : 4, 0, 0);
        return tb;
    }

    private static TextBlock RenderText(IReadOnlyList<MdInline> inlines, double size, IBrush brush, string? baseUrl)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = size,
            Foreground = brush,
            LineHeight = size * 1.45,
        };
        foreach (var inline in inlines)
            tb.Inlines!.Add(RenderInline(inline, size, baseUrl));
        return tb;
    }

    private static Inline RenderInline(MdInline inline, double size, string? baseUrl)
    {
        if (inline is MdLink link)
        {
            var target = GuideUrls.Resolve(baseUrl, link.Url);
            var linkBlock = new TextBlock
            {
                Text = link.Text,
                FontSize = size,
                Foreground = LinkFg,
                TextDecorations = TextDecorations.Underline,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(linkBlock, target ?? link.Url);
            linkBlock.PointerReleased += (_, _) => Services.Browser.Open(target);
            return new InlineUIContainer(linkBlock) { BaselineAlignment = BaselineAlignment.Baseline };
        }
        var t = (MdText)inline;
        var run = new Run(t.Text);
        if (t.Bold) run.FontWeight = FontWeight.Bold;
        if (t.Italic) run.FontStyle = FontStyle.Italic;
        // Only set what differs from the paragraph: assigning even a null Foreground would
        // override brush inheritance with "no brush" and render the text invisible.
        if (t.Code) { run.FontFamily = Mono; run.Foreground = CodeFg; }
        return run;
    }

    private static Control RenderList(MdList list, string? baseUrl)
    {
        var panel = new StackPanel { Spacing = 4 };
        foreach (var item in list.Items)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Margin = new Thickness(6 + item.Indent * 18, 0, 0, 0),
            };
            var marker = new TextBlock
            {
                Text = item.Marker,
                FontSize = 13,
                Foreground = Muted,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            var text = RenderText(item.Inlines, 13, Body, baseUrl);
            Grid.SetColumn(text, 1);
            grid.Children.Add(marker);
            grid.Children.Add(text);
            panel.Children.Add(grid);
        }
        return panel;
    }

    private static Control RenderCode(MdCodeBlock code) => new Border
    {
        Background = PanelBg,
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12, 8),
        Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new TextBlock
            {
                Text = code.Text,
                FontFamily = Mono,
                FontSize = 12,
                Foreground = CodeFg,
                LineHeight = 18,
            },
        },
    };

    private static Control RenderQuote(MdQuote quote, string? baseUrl)
    {
        var tb = RenderText(quote.Inlines, 13, Muted, baseUrl);
        return new Border
        {
            BorderBrush = Line,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 2, 0, 2),
            Child = tb,
        };
    }

    private static Control RenderImage(MdImage img, string? baseUrl)
    {
        var image = new Image
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,   // never upscale a small screenshot
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxHeight = 380,
        };
        var frame = new Border
        {
            Background = PanelBg,
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = image,
        };
        ToolTip.SetTip(frame, img.Alt.Length > 0 ? img.Alt : img.Url);
        if (GuideUrls.Resolve(baseUrl, img.Url) is { } url) _ = LoadImageAsync(image, url);
        return frame;
    }

    private static async Task LoadImageAsync(Image target, string url)
    {
        if (Http is not { } http) return;
        try
        {
            var bytes = await http.GetByteArrayAsync(url);
            var bmp = await Task.Run(() =>
            {
                using var ms = new MemoryStream(bytes);
                return new Bitmap(ms);
            });
            target.Source = bmp;
        }
        catch { /* image unreachable — leave the empty frame */ }
    }
}

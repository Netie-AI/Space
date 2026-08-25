using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace NetieSpace.Helpers;

/// <summary>
/// Lightweight Markdig → WPF for assistant chat bubbles (bold/italic/code/lists + mermaid fences).
/// </summary>
public static class ChatMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private static readonly Regex MermaidFence = new(
        @"```mermaid\s*\r?\n([\s\S]*?)```",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record Segment(bool IsMermaid, string Text);

    public static IReadOnlyList<Segment> SplitSegments(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return [new Segment(false, "")];

        var list = new List<Segment>();
        var last = 0;
        foreach (Match m in MermaidFence.Matches(markdown))
        {
            if (m.Index > last)
                list.Add(new Segment(false, markdown[last..m.Index]));
            list.Add(new Segment(true, m.Groups[1].Value.Trim()));
            last = m.Index + m.Length;
        }

        if (last < markdown.Length)
            list.Add(new Segment(false, markdown[last..]));

        return list.Count == 0 ? [new Segment(false, markdown)] : list;
    }

    public static FrameworkElement BuildBody(string markdown, Brush foreground)
    {
        var stack = new StackPanel();
        foreach (var seg in SplitSegments(markdown))
        {
            if (seg.IsMermaid)
                stack.Children.Add(BuildMermaidBlock(seg.Text, foreground));
            else if (!string.IsNullOrWhiteSpace(seg.Text))
                stack.Children.Add(BuildMarkdownBlock(seg.Text, foreground));
        }

        if (stack.Children.Count == 0)
            stack.Children.Add(BuildPlain(markdown, foreground));

        return stack;
    }

    private static TextBlock BuildPlain(string text, Brush foreground) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, SF Pro Text, Helvetica Neue"),
        Foreground = foreground,
        FontSize = 13.5,
        LineHeight = 19
    };

    private static FrameworkElement BuildMermaidBlock(string source, Brush foreground)
    {
        var box = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 4, 0, 4)
        };

        var inner = new StackPanel();
        inner.Children.Add(new TextBlock
        {
            Text = "mermaid",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(159, 212, 200)),
            Margin = new Thickness(0, 0, 0, 4)
        });
        inner.Children.Add(new TextBox
        {
            Text = source,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = foreground,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 12,
            MaxHeight = 180,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0)
        });
        box.Child = inner;
        box.Tag = source; // raw for copy
        return box;
    }

    private static FrameworkElement BuildMarkdownBlock(string markdown, Brush foreground)
    {
        var doc = Markdown.Parse(markdown, Pipeline);
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, SF Pro Text, Helvetica Neue"),
            Foreground = foreground,
            FontSize = 13.5,
            LineHeight = 19
        };

        var first = true;
        foreach (var block in doc)
        {
            if (!first)
                tb.Inlines.Add(new LineBreak());
            first = false;
            AppendBlock(tb.Inlines, block, foreground);
        }

        if (tb.Inlines.Count == 0)
            return BuildPlain(markdown, foreground);

        return tb;
    }

    private static void AppendBlock(InlineCollection inlines, Markdig.Syntax.Block block, Brush foreground)
    {
        switch (block)
        {
            case HeadingBlock heading:
                AppendLeaf(inlines, heading.Inline, bold: true, scale: heading.Level <= 2 ? 1.1 : 1.0, foreground);
                break;
            case ParagraphBlock para:
                AppendLeaf(inlines, para.Inline, bold: false, scale: 1.0, foreground);
                break;
            case CodeBlock code:
                inlines.Add(new Run(code.Lines.ToString().TrimEnd())
                {
                    FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
                    FontSize = 12.5,
                    Background = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0))
                });
                break;
            case ListBlock list:
                var i = 1;
                foreach (var item in list)
                {
                    if (item is not ListItemBlock li) continue;
                    inlines.Add(new Run(list.IsOrdered ? $"{i}. " : "• "));
                    i++;
                    foreach (var child in li)
                    {
                        AppendBlock(inlines, child, foreground);
                        inlines.Add(new LineBreak());
                    }
                }
                break;
            case QuoteBlock quote:
                foreach (var child in quote)
                    AppendBlock(inlines, child, foreground);
                break;
            default:
                // Fallback: dump text
                inlines.Add(new Run(block.ToString() ?? ""));
                break;
        }
    }

    private static void AppendLeaf(
        InlineCollection inlines,
        Markdig.Syntax.Inlines.ContainerInline? container,
        bool bold,
        double scale,
        Brush foreground)
    {
        if (container is null) return;
        foreach (var inline in container)
            AppendInline(inlines, inline, bold, scale, foreground);
    }

    private static void AppendInline(
        InlineCollection inlines,
        Markdig.Syntax.Inlines.Inline inline,
        bool bold,
        double scale,
        Brush foreground)
    {
        switch (inline)
        {
            case LiteralInline lit:
                inlines.Add(MakeRun(lit.Content.ToString(), bold, false, scale, foreground));
                break;
            case EmphasisInline em:
                var strong = em.DelimiterCount >= 2 || bold;
                var italic = em.DelimiterCount == 1;
                foreach (var child in em)
                    AppendInlineStyled(inlines, child, strong, italic, scale, foreground);
                break;
            case CodeInline code:
                inlines.Add(new Run(code.Content)
                {
                    FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
                    FontSize = 12.5 * scale,
                    Background = new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)),
                    Foreground = foreground
                });
                break;
            case LineBreakInline:
                inlines.Add(new LineBreak());
                break;
            case LinkInline link:
                foreach (var child in link)
                    AppendInline(inlines, child, bold, scale, foreground);
                if (!string.IsNullOrWhiteSpace(link.Url))
                    inlines.Add(MakeRun($" ({link.Url})", false, true, scale * 0.9, foreground));
                break;
            case ContainerInline container:
                foreach (var child in container)
                    AppendInline(inlines, child, bold, scale, foreground);
                break;
            default:
                inlines.Add(MakeRun(inline.ToString() ?? "", bold, false, scale, foreground));
                break;
        }
    }

    private static void AppendInlineStyled(
        InlineCollection inlines,
        Markdig.Syntax.Inlines.Inline inline,
        bool bold,
        bool italic,
        double scale,
        Brush foreground)
    {
        if (inline is LiteralInline lit)
        {
            var run = MakeRun(lit.Content.ToString(), bold, italic, scale, foreground);
            inlines.Add(run);
            return;
        }

        AppendInline(inlines, inline, bold, scale, foreground);
    }

    private static Run MakeRun(string text, bool bold, bool italic, double scale, Brush foreground) => new(text)
    {
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
        FontSize = 13.5 * scale,
        Foreground = foreground
    };

    public static string CollectCopyText(string markdown) => markdown ?? "";
}

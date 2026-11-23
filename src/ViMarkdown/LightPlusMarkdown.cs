using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Markdig;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace ViMarkdown;

// Color source spans from the Markdown parser, keeping every delimiter visible.
// Parse once per document version; cache only the spans needed by each line.
public sealed class LightPlusMarkdown : DocumentColorizingTransformer, IBackgroundRenderer
{
    private sealed record Span(int Start, int End, IBrush Foreground, IBrush? Background);
    private readonly Dictionary<int, List<Span>> _lines = [];
    private TextDocument? _document;
    private ITextSourceVersion? _version;
    private static readonly MarkdownPipeline Pipeline = CreatePipeline();
    private static MarkdownPipeline CreatePipeline()
    {
        var builder = new MarkdownPipelineBuilder()
        .UsePipeTables().UseTaskLists().UseDefinitionLists()
        .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
        .UsePreciseSourceLocation().EnableTrackTrivia();
        return builder.UseAdjacentReferenceLinks().Build();
    }
    private static readonly IBrush Heading = Brush("#005CC5");
    private static readonly IBrush Emphasis = Brush("#7A3E9D");
    private static readonly IBrush Link = Brush("#007ACC");
    private static readonly IBrush Destination = Brush("#5B6573");
    private static readonly IBrush Code = Brush("#A43B00");
    private static readonly IBrush CodeBackground = WorkspaceColors.CodeBackground;
    private static readonly IBrush Marker = Brush("#16754A");
    private static readonly IBrush Quote = Brush("#667085");
    private static IBrush Brush(string color) => new ImmutableSolidColorBrush(Color.Parse(color));

    // Text-run backgrounds are drawn with the text, above the selection layer.
    // Reuse the parsed spans on the background layer so selection stays visible.
    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || textView.Document is not { } document) return;
        EnsureParsed(document);
        foreach (var visual in textView.VisualLines)
        {
            for (var number = visual.FirstDocumentLine.LineNumber;
                number <= visual.LastDocumentLine.LineNumber; number++)
            {
                if (!_lines.TryGetValue(number, out var spans)) continue;
                foreach (var span in spans)
                {
                    if (span.Background is null) continue;
                    var segment = new SimpleSegment(span.Start, span.End - span.Start);
                    foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                        drawingContext.FillRectangle(span.Background, rect);
                }
            }
        }
    }

    private void EnsureParsed(TextDocument document)
    {
        if (!ReferenceEquals(_document, document) || !ReferenceEquals(_version, document.Version))
            Parse(document);
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var document = CurrentContext.Document;
        EnsureParsed(document);
        if (!_lines.TryGetValue(line.LineNumber, out var spans)) return;
        foreach (var span in spans)
            ChangeLinePart(span.Start, span.End, element =>
            {
                element.TextRunProperties.SetForegroundBrush(span.Foreground);
                if (ReferenceEquals(span.Foreground, Code))
                    element.TextRunProperties.SetTypeface(Typography.EditorTypeface);
            });
    }

    private void Parse(TextDocument document)
    {
        _document = document;
        _version = document.Version;
        _lines.Clear();
        var source = document.Text;
        void Paint(int start, int end, IBrush color, IBrush? background = null)
        {
            start = Math.Clamp(start, 0, source.Length);
            end = Math.Clamp(end, start, source.Length);
            if (start == end) return;
            var line = document.GetLineByOffset(start);
            while (line is not null && line.Offset < end)
            {
                var lo = Math.Max(start, line.Offset);
                var hi = Math.Min(end, line.EndOffset);
                if (lo < hi)
                {
                    if (!_lines.TryGetValue(line.LineNumber, out var spans)) _lines[line.LineNumber] = spans = [];
                    spans.Add(new(lo, hi, color, background));
                }
                line = line.NextLine;
            }
        }
        void Inlines(ContainerInline container)
        {
            foreach (var node in container)
            {
                switch (node)
                {
                    case CodeInline code:
                        Paint(code.Span.Start, code.Span.End + 1, Code, CodeBackground);
                        break;
                    case EmphasisInline emphasis:
                        Paint(emphasis.Span.Start, emphasis.Span.End + 1, Emphasis);
                        Inlines(emphasis);
                        break;
                    case LinkInline link:
                        Paint(link.Span.Start, link.Span.End + 1, Link);
                        if (link.Reference is null && link.UrlSpan.Start >= link.Span.Start && link.UrlSpan.End <= link.Span.End)
                            Paint(link.UrlSpan.Start, link.UrlSpan.End + 1, Destination);
                        Inlines(link);
                        break;
                    case TaskList task:
                        var start = source.IndexOf('[', Math.Clamp(task.Span.Start, 0, source.Length));
                        if (start >= 0) Paint(start, start + 3, Marker);
                        break;
                    case ContainerInline nested:
                        Inlines(nested);
                        break;
                }
            }
        }
        void Blocks(ContainerBlock container)
        {
            foreach (var block in container)
            {
                switch (block)
                {
                    case CodeBlock code:
                        // All languages, including D2, use the same code color.
                        // An unfinished fence can have a span ending at its opener
                        // while its content lines already extend to end-of-document.
                        var codeEnd = code.Lines.Lines.Take(code.Lines.Count)
                            .Select(l => l.Slice.End + 1).DefaultIfEmpty(code.Span.End + 1).Max();
                        Paint(code.Span.Start, Math.Max(code.Span.End + 1, codeEnd), Code, CodeBackground);
                        continue;
                    case HeadingBlock { IsSetext: true } underlined:
                        if (underlined.Level == 2)
                        {
                            var ruleStart = Math.Clamp(block.Span.End, 0, source.Length);
                            while (ruleStart > block.Span.Start && source[ruleStart - 1] is not ('\r' or '\n')) ruleStart--;
                            Paint(ruleStart, block.Span.End + 1, Marker);
                        }
                        break;
                    case HeadingBlock:
                    case DefinitionTerm:
                        Paint(block.Span.Start, block.Span.End + 1, Heading);
                        break;
                    case QuoteBlock:
                        Paint(block.Span.Start, block.Span.End + 1, Quote);
                        break;
                    case ThematicBreakBlock:
                        Paint(block.Span.Start, block.Span.End + 1, Marker);
                        break;
                    case ListItemBlock:
                        var end = block.Span.Start;
                        while (end < source.Length && source[end] is not (' ' or '\t' or '\r' or '\n')) end++;
                        Paint(block.Span.Start, end, Marker);
                        break;
                    case Table table:
                        for (var i = table.Span.Start; i <= table.Span.End && i < source.Length; i++)
                            if (source[i] == '|' && (i == 0 || source[i - 1] != '\\')) Paint(i, i + 1, Marker);
                        foreach (var line in document.Lines.Where(l => l.Offset >= table.Span.Start && l.Offset <= table.Span.End))
                        {
                            var text = document.GetText(line);
                            if (text.Contains('-') && text.All(c => c is ' ' or '\t' or '|' or ':' or '-'))
                                Paint(line.Offset, line.EndOffset, Marker);
                        }
                        break;
                    case DefinitionItem:
                        var colon = block.Span.Start;
                        if (colon >= 0 && colon < source.Length && source[colon] == ':') Paint(colon, colon + 1, Marker);
                        break;
                }
                if (block is LeafBlock { Inline: not null } leaf) Inlines(leaf.Inline);
                if (block is ContainerBlock nested) Blocks(nested);
            }
        }
        Blocks(Markdown.Parse(source, Pipeline));
    }
}

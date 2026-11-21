using System.Globalization;
using Markdig;
using Markdig.Syntax;
using Markdig.Extensions.Tables;
using Markdig.Syntax.Inlines;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.DefinitionLists;
using Avalonia.Media;

namespace ViMarkdown;

public record DisplayUnit(string Text, int Start, int End, double Size = Typography.Body,
    bool Bold = false, bool Italic = false, bool Code = false,
    bool Strike = false, string? LinkUrl = null, int? TaskOffset = null, bool Rule = false,
    IReadOnlyList<TextAlignment>? Alignments = null, double HeadingRuleThickness = 0, double BottomSpacing = 0,
    string? ImageUrl = null, string? MermaidCode = null, IReadOnlyList<IReadOnlyList<IReadOnlyList<DisplayUnit>>>? Table = null);

// Source offsets are UTF-16, matching AvaloniaEdit. Grapheme boundaries prevent
// navigation from splitting surrogate pairs, combining marks or emoji sequences.
public static class TextBoundaries
{
    public static int Next(string text, int offset)
        => StringInfo.ParseCombiningCharacters(text).FirstOrDefault(x => x > offset, text.Length);
    public static int Previous(string text, int offset)
        => StringInfo.ParseCombiningCharacters(text).LastOrDefault(x => x < offset, 0);
}

public static class Projection
{
    private static readonly MarkdownPipeline Pipeline = CreatePipeline();
    private static MarkdownPipeline CreatePipeline()
    {
        var builder = new MarkdownPipelineBuilder()
        .UsePipeTables().UseTaskLists().UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough).UseDefinitionLists().UsePreciseSourceLocation();
        return builder.UseAdjacentReferenceLinks().Build();
    }

    public static IReadOnlyList<DisplayUnit> Build(string source)
    {
        var output = new List<DisplayUnit>();
        void Add(string text, int start, double size = Typography.Body, bool bold = false,
            bool italic = false, bool code = false)
        {
            var indices = StringInfo.ParseCombiningCharacters(text);
            for (var i = 0; i < indices.Length; i++)
            {
                var end = i + 1 < indices.Length ? indices[i + 1] : text.Length;
                output.Add(new(text[indices[i]..end], Math.Clamp(start + indices[i], 0, source.Length),
                    Math.Clamp(start + end, 0, source.Length), size, bold, italic, code));
            }
        }
        void Inline(ContainerInline container, double size, bool bold = false, bool italic = false, bool strike = false, string? linkUrl = null)
        {
            var firstUnit = output.Count;
            foreach (var node in container)
            {
                switch (node)
                {
                    case LiteralInline literal:
                        Add(literal.Content.ToString(), literal.Span.Start, size, bold, italic);
                        break;
                    case CodeInline code:
                        // The span includes delimiters; content starts after opening backticks.
                        var start = code.Span.Start;
                        while (start < source.Length && source[start] == '`') start++;
                        while (start < code.Span.End && source[start] == ' ' && code.Content.Length > 0
                               && source[start..].StartsWith(code.Content) == false) start++;
                        Add(code.Content, start, size, bold, italic, true);
                        break;
                    case LineBreakInline line:
                        // This editor preserves source line breaks even when
                        // Markdown classifies them as soft breaks. Normalize
                        // their display to LF while mapping the entire CRLF.
                        var breakStart = Math.Clamp(line.Span.Start, 0, source.Length);
                        while (breakStart < source.Length && source[breakStart] is not ('\r' or '\n'))
                            breakStart++;
                        var breakEnd = breakStart;
                        if (breakEnd < source.Length)
                        {
                            breakEnd++;
                            if (source[breakStart] == '\r' && breakEnd < source.Length && source[breakEnd] == '\n')
                                breakEnd++;
                        }
                        output.Add(new("\n", breakStart, breakEnd, size, bold, italic));
                        break;
                    case EmphasisInline emphasis:
                        Inline(emphasis, size, bold || (emphasis.DelimiterChar != '~' && emphasis.DelimiterCount == 2),
                            italic || (emphasis.DelimiterChar != '~' && emphasis.DelimiterCount == 1),
                            strike || emphasis.DelimiterChar == '~', linkUrl);
                        break;
                    case LinkInline link when link.IsImage:
                        output.Add(new("[画像]", link.Span.Start, link.Span.End + 1, size, ImageUrl: link.Url ?? ""));
                        break;
                    case TaskList task:
                        var taskStart = source.IndexOf('[', Math.Clamp(task.Span.Start, 0, source.Length));
                        if (taskStart < 0) taskStart = task.Span.Start;
                        output.Add(new(task.Checked ? "☑" : "☐", taskStart, Math.Min(source.Length, taskStart + 3),
                            Math.Max(size, Typography.Checkbox), TaskOffset: taskStart));
                        break;
                    case LinkInline link:
                        Inline(link, size, bold, italic, strike, link.Url);
                        break;
                    case ContainerInline nested:
                        Inline(nested, size, bold, italic, strike, linkUrl);
                        break;
                    default:
                        if (node.Span.Start >= 0 && node.Span.End < source.Length)
                            Add(source[node.Span.Start..(node.Span.End + 1)], node.Span.Start, size);
                        break;
                }
            }
            for (var i = firstUnit; i < output.Count; i++)
                output[i] = output[i] with { Strike = output[i].Strike || strike, LinkUrl = output[i].LinkUrl ?? linkUrl };
        }
        // List container spans can include trailing blank lines. Measure the
        // source gap from the final content block so those lines are not lost.
        int ContentEnd(Block block)
            => block is ContainerBlock { Count: > 0 } container
                ? ContentEnd(container[container.Count - 1]) : block.Span.End + 1;

        void AddSourceGap(Block previous, int nextStart)
        {
            var gapStart = Math.Clamp(previous is ListBlock ? ContentEnd(previous) : previous.Span.End + 1, 0, source.Length);
            var gapEnd = Math.Clamp(nextStart, gapStart, source.Length);
            var firstBreakEnd = gapStart;
            var breaks = 0;
            for (var i = gapStart; i < gapEnd; i++)
            {
                if (source[i] is not ('\r' or '\n')) continue;
                if (source[i] == '\r' && i + 1 < gapEnd && source[i + 1] == '\n') i++;
                if (++breaks == 1) firstBreakEnd = i + 1;
            }
            // Preserve the existing collapsed paragraph gap separately from
            // fixed block spacing, with its real source range for navigation.
            if (breaks >= 2)
                output.Add(new("\n", firstBreakEnd, gapEnd));
        }

        IReadOnlyList<DisplayUnit> PrefixUnits(string prefix, int contentOffset)
        {
            if (!prefix.Contains('│')) return new[] { new DisplayUnit(prefix, contentOffset, contentOffset) };
            var lineStart = contentOffset;
            while (lineStart > 0 && source[lineStart - 1] is not ('\r' or '\n')) lineStart--;
            var cursor = lineStart;
            var result = new List<DisplayUnit>();
            for (var i = 0; i < prefix.Length;)
            {
                if (prefix[i] == '│')
                {
                    while (cursor < contentOffset && source[cursor] is ' ' or '\t') cursor++;
                    if (cursor >= contentOffset || source[cursor] != '>')
                        return new[] { new DisplayUnit(prefix, contentOffset, contentOffset) };
                    result.Add(new("│", cursor, cursor + 1));
                    cursor++;
                    i++;
                }
                else
                {
                    var start = i;
                    while (i < prefix.Length && prefix[i] != '│') i++;
                    var sourceStart = cursor;
                    while (cursor < contentOffset && source[cursor] is ' ' or '\t') cursor++;
                    result.Add(new(prefix[start..i], sourceStart, cursor));
                }
            }
            return result;
        }

        void Blocks(ContainerBlock blocks, string prefix = "")
        {
            Block? previous = null;
            var listIndex = blocks is ListBlock list && int.TryParse(list.OrderedStart, out var number) ? number : 1;
            // Markdig collects reference definitions in a document-level group.
            // Its synthetic span is not a source position: projecting that group
            // as a block can append a newline mapped back to the document start.
            // Merge the actual definitions with the body in source order so both
            // hit testing and source-to-display lookup follow the visible lines.
            var orderedBlocks = blocks.SelectMany(block => block is LinkReferenceDefinitionGroup group
                ? group.Cast<Block>() : new[] { block }).OrderBy(block => block.Span.Start);
            foreach (var block in orderedBlocks)
            {
                if (previous is not null) AddSourceGap(previous, block.Span.Start);
                var contentStart = output.Count;
                if (block is ThematicBreakBlock)
                    output.Add(new("", block.Span.Start, block.Span.End + 1, Rule: true));
                else if (block is Table table)
                {
                    var rows = new List<IReadOnlyList<IReadOnlyList<DisplayUnit>>>();
                    foreach (var row in table.OfType<TableRow>())
                    {
                        var cells = new List<IReadOnlyList<DisplayUnit>>();
                        foreach (var cell in row.OfType<TableCell>())
                        {
                            var saved = output.ToArray(); output.Clear();
                            foreach (var paragraph in cell.OfType<LeafBlock>())
                                if (paragraph.Inline is not null) Inline(paragraph.Inline, Typography.Body, row.IsHeader);
                            // Pipe-table parsing may include the leading cell space in
                            // inline spans. Align visible literals with their source text.
                            for (var i = 0; i < output.Count; i++)
                            {
                                var unit = output[i];
                                if (unit.Text.Length > 0 && unit.End <= source.Length &&
                                    !source.AsSpan(unit.Start, unit.End - unit.Start).SequenceEqual(unit.Text) &&
                                    unit.Start + 1 + unit.Text.Length <= source.Length &&
                                    source.AsSpan(unit.Start + 1, unit.Text.Length).SequenceEqual(unit.Text))
                                    output[i] = unit with { Start = unit.Start + 1, End = unit.Start + 1 + unit.Text.Length };
                            }
                            if (output.Count == 0) output.Add(new(" ", cell.Span.Start, cell.Span.Start));
                            cells.Add(output.ToArray()); output.Clear(); output.AddRange(saved);
                        }
                        rows.Add(cells);
                    }
                    output.Add(new("", block.Span.Start, block.Span.End + 1, Table: rows,
                        Alignments: table.ColumnDefinitions.Select(c => c.Alignment switch {
                            TableColumnAlign.Center => TextAlignment.Center,
                            TableColumnAlign.Right => TextAlignment.Right,
                            _ => TextAlignment.Left }).ToArray()));
                }
                else if (block is HeadingBlock { IsSetext: true, Inline: not null } underlined)
                {
                    // Keep the parser's separation of body text and underline,
                    // but display a dash underline as a standalone horizontal rule.
                    Inline(underlined.Inline, Typography.Body);
                    if (underlined.Level == 2)
                    {
                        var ruleStart = Math.Clamp(block.Span.End, 0, source.Length);
                        while (ruleStart > block.Span.Start && source[ruleStart - 1] is not ('\r' or '\n')) ruleStart--;
                        Add("\n", Math.Max(block.Span.Start, ruleStart - 1));
                        output.Add(new("", ruleStart, block.Span.End + 1, Rule: true));
                    }
                }
                else if (block is LeafBlock { Inline: not null } leaf)
                    Inline(leaf.Inline, block is HeadingBlock h ? (32 - h.Level * 2) * Typography.RichScale : Typography.Body,
                        block is HeadingBlock || block is DefinitionTerm);
                else if (block is FencedCodeBlock mermaid && MermaidRenderer.IsMermaid(mermaid))
                    output.Add(new("", block.Span.Start, block.Span.End + 1, MermaidCode: MermaidRenderer.Code(mermaid)));
                else if (block is CodeBlock code)
                {
                    foreach (var line in code.Lines.Lines.Take(code.Lines.Count))
                    {
                        Add(line.Slice.ToString(), line.Slice.Start, Typography.Code, code: true);
                        Add("\n", Math.Min(source.Length, line.Slice.End + 1));
                    }
                }
                else if (block is ListBlock listBlock) Blocks(listBlock, prefix);
                else if (block is ListItemBlock item)
                {
                    var marker = blocks is ListBlock { IsOrdered: true } ? $"{listIndex++}. " : "• ";
                    Blocks(item, prefix + "    ");
                    var first = output.FindIndex(contentStart, u => u.Text.Trim().Length > 0);
                    if (first >= 0)
                    {
                        // Replace the first continuation indent with the item marker.
                        if (output[contentStart].Text == prefix + "    ") output.RemoveAt(contentStart);
                        var markerEnd = Math.Max(item.Span.Start, output[contentStart].Start);
                        output.Insert(contentStart, new(prefix + marker, item.Span.Start, markerEnd));
                    }
                }
                else if (block is QuoteBlock quote) Blocks(quote, prefix + "│  ");
                else if (block is DefinitionItem definition) Blocks(definition, prefix);
                else if (block is ContainerBlock container) Blocks(container, prefix);
                else if (block.Span.Start >= 0 && block.Span.End < source.Length)
                    Add(source[block.Span.Start..(block.Span.End + 1)], block.Span.Start);
                // Attach the divider to the final heading glyph so wrapping places
                // it below the last visual line without adding source characters.
                if (block is HeadingBlock { IsSetext: false, Level: 1 or 2 or 3 } heading && output.Count > contentStart)
                    output[^1] = output[^1] with { HeadingRuleThickness = heading.Level == 1 ? 2 : 1 };
                if (block is LeafBlock && prefix.Length > 0)
                {
                    for (var i = output.Count - 1; i >= contentStart; i--)
                        if (i == contentStart || output[i - 1].Text.EndsWith('\n'))
                            output.InsertRange(i, PrefixUnits(prefix, output[i].Start));
                }
                // Horizontal rules already reserve and terminate one visual line
                // in RichTextLayout. Another newline would add a synthetic blank.
                // Code lines and nested blocks also already terminate their output.
                if (output.Count > 0 && !output[^1].Rule && !output[^1].Text.EndsWith('\n'))
                    Add("\n", Math.Min(source.Length, block.Span.End + 1));
                if ((block is HeadingBlock { IsSetext: false, Level: <= 3 } ||
                    block is ListBlock && blocks is not ListItemBlock) && output.Count > contentStart)
                    output[^1] = output[^1] with { BottomSpacing = Typography.HeadingSpacing };
                previous = block;
            }
            if (blocks is MarkdownDocument && previous is ListBlock)
                AddSourceGap(previous, source.Length);
        }
        Blocks(Markdown.Parse(source, Pipeline));
        if (output.Count == 0) output.Add(new(" ", 0, 0));
        return output;
    }
}

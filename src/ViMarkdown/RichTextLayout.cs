using System.Text;
using Avalonia.Media.Imaging;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;

namespace ViMarkdown;

// The displayed string has its own UTF-16 offsets, separate from the Markdown.
// Shape whole style runs together so fallback fonts, kerning and baselines are
// resolved by Avalonia rather than by independently positioned characters.
public sealed class RichTextLayout : IDisposable
{
    private record MappedUnit(DisplayUnit Unit, int Start, int Length);
    private readonly List<MappedUnit> _mapping = [];
    private sealed record ObjectLayout(DisplayUnit Unit, int Offset, double Height, Bitmap? Image, Size ImageSize,
        List<(RichTextLayout Layout, Rect Rect)> Cells, string? Error, bool Pending);
    private readonly List<ObjectLayout> _objects = [];
    private readonly double _width;
    private const double CellPaddingY = 4;
    private const double ImageLeftPadding = 4;
    public string Text { get; }
    public TextLayout Layout { get; }
    private readonly double[] _lineTops;
    private readonly double[] _lineShifts;
    private readonly double[] _lineHeights;
    public double Height { get; }
    public Vector FirstLineInkInset { get; }

    public RichTextLayout(IReadOnlyList<DisplayUnit> units, double width, Func<string, byte[]?>? imageResolver = null, double lineHeight = double.NaN,
        Func<string, D2Result?>? d2Resolver = null, TextAlignment alignment = TextAlignment.Left)
    {
        _width = Math.Max(1, width);
        // Let each text line follow its font metrics (including headings and
        // fallback fonts). Object placeholders use the same body-line metrics.
        using var bodyLine = new TextLayout(" ", Typography.BodyTypeface, Typography.Body, WorkspaceColors.Text,
            lineHeight: lineHeight);
        var objectLineHeight = bodyLine.TextLines[0].Height;
        var text = new StringBuilder();
        var styles = new List<ValueSpan<TextRunProperties>>();
        var diagnosticBlocks = new List<(int Offset, int Length, double Height, bool TrimFollowingGap)>();
        DisplayUnit? previousStyle = null;
        var styleStart = 0;
        void FinishStyle()
        {
            if (previousStyle is null || text.Length == styleStart) return;
            var typeface = new Typeface(previousStyle.Code ? Typography.EditorFont : Typography.BodyFont,
                previousStyle.Italic ? FontStyle.Italic : FontStyle.Normal,
                previousStyle.Bold ? FontWeight.Bold : FontWeight.Normal);
            styles.Add(new ValueSpan<TextRunProperties>(styleStart, text.Length - styleStart,
                new GenericTextRunProperties(typeface, previousStyle.Size,
                    baselineAlignment: previousStyle.TaskOffset.HasValue ? BaselineAlignment.Center : BaselineAlignment.Baseline,
                    foregroundBrush: previousStyle.LinkUrl is not null ? WorkspaceColors.Accent : previousStyle.Code ? WorkspaceColors.Code : WorkspaceColors.Text,
                    textDecorations: previousStyle.Strike && previousStyle.LinkUrl is not null
                        ? new TextDecorationCollection { TextDecorations.Strikethrough[0], TextDecorations.Underline[0] }
                        : previousStyle.Strike ? TextDecorations.Strikethrough
                        : previousStyle.LinkUrl is not null ? TextDecorations.Underline : null)));
        }
        foreach (var original in units)
        {
            var unit = original;
            if (unit.Rule || unit.Table is not null || unit.ImageUrl is not null || unit.D2Code is not null)
            {
                // Objects occupy complete visual lines; source spans remain atomic.
                if (text.Length > 0 && text[^1] != '\n') text.Append('\n');
                var cells = new List<(RichTextLayout Layout, Rect Rect)>();
                Bitmap? bitmap = null;
                var imageSize = default(Size);
                double height = 84;
                string? error = null;
                var pending = false;
                if (unit.Table is { Count: > 0 } rows)
                {
                    var columns = Math.Max(1, rows.Max(r => r.Count));
                    var cellWidth = _width / columns;
                    height = 0;
                    foreach (var row in rows)
                    {
                        var layouts = row.Select((c, i) => new RichTextLayout(c, Math.Max(1, cellWidth - 16), imageResolver, lineHeight, d2Resolver, unit.Alignments?.ElementAtOrDefault(i) ?? TextAlignment.Left)).ToArray();
                        var rowHeight = layouts.Select(l => l.Height).DefaultIfEmpty(objectLineHeight).Max() + CellPaddingY * 2;
                        for (var i = 0; i < layouts.Length; i++)
                            cells.Add((layouts[i], new Rect(i * cellWidth, height, cellWidth, rowHeight)));
                        height += rowHeight;
                    }
                }
                else if (unit.Rule) height = objectLineHeight;
                else
                {
                    try
                    {
                        var result = unit.D2Code is null ? null : d2Resolver?.Invoke(unit.D2Code);
                        pending = unit.D2Code is not null && result is null;
                        error = unit.D2Code is null ? null : result?.Error ?? (result?.Png is null ? "D2を描画中…" : null);
                        var data = unit.D2Code is null ? imageResolver?.Invoke(unit.ImageUrl!) : result?.Png;
                        if (data is not null) bitmap = new Bitmap(new MemoryStream(data));
                        if (bitmap is not null)
                        {
                            imageSize = ImageContent.DisplaySize(bitmap, data!, Math.Max(1, _width - ImageLeftPadding));
                            height = imageSize.Height + 12;
                        }
                    }
                    catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { }
                }
                if (error is not null && !pending)
                {
                    using var label = new TextLayout(error, Typography.BodyTypeface, Typography.Diagnostic, WorkspaceColors.Diagnostic,
                        textWrapping: TextWrapping.Wrap, maxWidth: Math.Max(1, _width - 16));
                    height = label.Height + objectLineHeight;
                }
                var objectLines = (int)Math.Ceiling(height / objectLineHeight);
                var reservedHeight = objectLines * objectLineHeight;
                if (error is not null)
                    diagnosticBlocks.Add((text.Length, objectLines, height, !pending));
                else
                    height = reservedHeight;
                _objects.Add(new(unit, text.Length, height, bitmap, imageSize, cells, error, pending));
                unit = unit with { Text = new string('\n', objectLines), Size = Typography.Body, Bold = false, Italic = false, Code = false };
            }
            if (previousStyle is null || !SameStyle(previousStyle, unit))
            {
                FinishStyle();
                previousStyle = unit;
                styleStart = text.Length;
            }
            var displayed = unit.Text == "\t" ? "    " : unit.Text;
            _mapping.Add(new(unit, text.Length, displayed.Length));
            text.Append(displayed);
        }
        FinishStyle();
        Text = text.ToString();
        Layout = new TextLayout(Text, Typography.BodyTypeface, Typography.Body, WorkspaceColors.Text,
            textWrapping: TextWrapping.Wrap, maxWidth: Math.Max(1, width), lineHeight: lineHeight, textAlignment: alignment,
            textStyleOverrides: styles);
        var lines = Layout.TextLines;
        _lineHeights = lines.Select(line => line.Height).ToArray();
        foreach (var block in diagnosticBlocks)
        {
            var first = Layout.GetLineIndexFromCharacterIndex(block.Offset, false);
            var last = Layout.GetLineIndexFromCharacterIndex(block.Offset + block.Length - 1, false);
            var actualHeight = _lineHeights.Skip(first).Take(last - first + 1).Sum();
            // Empty TextLayout lines can inherit adjacent run metrics. Normalize
            // the actual reserved lines, rather than assuming body-sized lines.
            for (var index = first; index <= last; index++)
                _lineHeights[index] *= block.Height / actualHeight;
            if (!block.TrimFollowingGap) continue;
            // Projection emits a block terminator and a collapsed source gap.
            // The diagnostic already owns its one-line bottom margin. Keep the
            // source mappings for editing, without reserving those lines twice.
            var next = block.Offset + block.Length;
            foreach (var item in _mapping.Where(m => m.Start >= next))
            {
                if (item.Start != next || item.Length == 0 || item.Unit.D2Code is not null ||
                    item.Unit.ImageUrl is not null || item.Unit.Table is not null || item.Unit.Rule ||
                    !item.Unit.Text.All(c => c is '\r' or '\n')) break;
                var gapFirst = Layout.GetLineIndexFromCharacterIndex(item.Start, false);
                var gapLast = Layout.GetLineIndexFromCharacterIndex(item.Start + item.Length - 1, false);
                for (var index = gapFirst; index <= gapLast; index++) _lineHeights[index] = 0;
                next += item.Length;
            }
        }
        _lineTops = new double[lines.Count];
        _lineShifts = new double[lines.Count];
        var spacing = new double[lines.Count];
        foreach (var item in _mapping.Where(m => m.Unit.BottomSpacing > 0))
        {
            var index = Layout.GetLineIndexFromCharacterIndex(item.Start + Math.Max(0, item.Length - 1), false);
            spacing[index] = Math.Max(spacing[index], item.Unit.BottomSpacing);
        }
        double top = 0, shift = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            _lineTops[i] = top + shift;
            _lineShifts[i] = shift;
            top += lines[i].Height;
            shift += spacing[i] + _lineHeights[i] - lines[i].Height;
        }
        Height = top + shift;
        FirstLineInkInset = TextInkAlignment.Inset(Layout.TextLines[0], Text);
    }

    private Rect TextBounds(int offset)
    {
        var rect = Layout.HitTestTextPosition(offset);
        var index = Layout.GetLineIndexFromCharacterIndex(offset, false);
        return rect.Translate(new Vector(0, _lineShifts[index]));
    }

    private IEnumerable<Rect> TextRangeBounds(int start, int length)
        => Layout.HitTestTextRange(start, length).Select(rect =>
        {
            var index = 0;
            double top = 0;
            while (index + 1 < Layout.TextLines.Count && top + Layout.TextLines[index].Height <= rect.Y + .001)
                top += Layout.TextLines[index++].Height;
            return rect.Translate(new Vector(0, _lineShifts[index]));
        });

    private double RawY(double y)
    {
        for (var i = 0; i < _lineTops.Length; i++)
        {
            var bottom = _lineTops[i] + _lineHeights[i];
            if (y < bottom) return y - _lineShifts[i];
            if (i + 1 < _lineTops.Length && y < _lineTops[i + 1])
                return bottom - _lineShifts[i] - .001;
        }
        return y - _lineShifts[^1];
    }

    public void DrawBackground(DrawingContext context)
    {
        foreach (var item in _mapping.Where(m => m.Unit.Code))
            foreach (var rect in TextRangeBounds(item.Start, item.Length))
                context.FillRectangle(WorkspaceColors.CodeBackground, rect);
        foreach (var obj in _objects.Where(o => o.Unit.Table is not null))
            foreach (var (cell, bounds) in obj.Cells)
                using (context.PushTransform(Matrix.CreateTranslation(bounds.X + 8,
                    TextBounds(obj.Offset).Y + bounds.Y + CellTop(cell, bounds))))
                    cell.DrawBackground(context);
    }

    public void Draw(DrawingContext context)
    {
        for (var i = 0; i < Layout.TextLines.Count; i++)
            Layout.TextLines[i].Draw(context, new Point(0, _lineTops[i]));
        foreach (var heading in _mapping.Where(m => m.Unit.HeadingRuleThickness > 0))
        {
            var thickness = heading.Unit.HeadingRuleThickness;
            var lastLine = TextBounds(heading.Start);
            var y = lastLine.Bottom - thickness / 2;
            context.DrawLine(new Pen(WorkspaceColors.Divider, thickness), new Point(0, y), new Point(_width, y));
        }
        foreach (var obj in _objects)
        {
            var top = TextBounds(obj.Offset).Y;
            if (obj.Unit.Rule)
                context.DrawLine(new Pen(WorkspaceColors.Divider, 1), new Point(0, top + obj.Height / 2), new Point(_width, top + obj.Height / 2));
            else if (obj.Unit.Table is not null)
            {
                foreach (var (cell, rect) in obj.Cells)
                {
                    var bounds = rect.Translate(new Vector(0, top));
                    context.DrawRectangle(null, new Pen(WorkspaceColors.Divider), bounds);
                    using (context.PushTransform(Matrix.CreateTranslation(bounds.X + 8, bounds.Y + CellTop(cell, bounds)))) cell.Draw(context);
                }
            }
            else if (obj.Image is { } image)
            {
                context.DrawImage(image, new Rect(new Point(ImageLeftPadding, top), obj.ImageSize));
            }
            else
            {
                using var label = new TextLayout(obj.Error ?? "[画像を読み込めません]", Typography.BodyTypeface, Typography.Diagnostic, WorkspaceColors.Diagnostic,
                    textWrapping: TextWrapping.Wrap, maxWidth: Math.Max(1, _width - 16));
                label.Draw(context, new Point(8, top + (obj.Pending || obj.Error is null ? 8 : 0)));
            }
        }
    }

    private static double CellTop(RichTextLayout cell, Rect bounds)
        => (bounds.Height - cell.Height) / 2;

    private bool ContainsSource(int offset)
        => _mapping.Any(m => offset >= m.Unit.Start && (offset < m.Unit.End || offset == m.Unit.Start))
            || _mapping.LastOrDefault()?.Unit.End == offset;

    private int HitTestCell(Point point)
    {
        var hit = HitTest(point);
        return _mapping.Count == 0 ? hit : Math.Clamp(hit, _mapping[0].Unit.Start, _mapping[^1].Unit.End);
    }

    public bool TryMoveInTable(int sourceOffset, int direction, bool vertical, out int target)
    {
        target = sourceOffset;
        var table = _objects.FirstOrDefault(o => o.Unit.Table is not null &&
            sourceOffset >= o.Unit.Start && sourceOffset < o.Unit.End);
        if (table is null) return false;
        if (vertical)
            target = MoveVertical(sourceOffset, direction);
        else
        {
            var positions = table.Cells.SelectMany(c => c.Layout._mapping.Select(m => m.Unit.Start)
                .Concat(c.Layout._mapping.Count == 0 ? Array.Empty<int>() : new[] { c.Layout._mapping[^1].Unit.End }))
                .Distinct().OrderBy(p => p).ToArray();
            target = direction > 0 ? positions.FirstOrDefault(p => p > sourceOffset, sourceOffset)
                : positions.LastOrDefault(p => p < sourceOffset, sourceOffset);
            if (direction > 0 && positions.Length > 0 && sourceOffset == positions[^1])
                target = table.Unit.End;
        }
        return true;
    }

    private static bool SameStyle(DisplayUnit a, DisplayUnit b)
        => a.Size == b.Size && a.Bold == b.Bold && a.Italic == b.Italic && a.Code == b.Code && a.Strike == b.Strike && a.LinkUrl == b.LinkUrl
            && a.TaskOffset.HasValue == b.TaskOffset.HasValue;

    public int ToDisplayOffset(int sourceOffset)
    {
        foreach (var item in _mapping)
            if (sourceOffset <= item.Unit.Start || sourceOffset < item.Unit.End)
                return item.Start;
        return Text.Length;
    }

    private int ToSourceOffset(int displayOffset, bool trailing)
    {
        displayOffset = Math.Clamp(displayOffset, 0, Text.Length);
        foreach (var item in _mapping)
        {
            if (trailing && displayOffset == item.Start + item.Length) return item.Unit.End;
            if (displayOffset < item.Start + item.Length)
            {
                // Atomic objects, tabs and grapheme clusters cannot be split by clicks.
                return displayOffset - item.Start > item.Length / 2.0 ? item.Unit.End : item.Unit.Start;
            }
        }
        return _mapping.LastOrDefault()?.Unit.End ?? 0;
    }

    public Rect CaretBounds(int sourceOffset)
    {
        foreach (var obj in _objects.Where(o => o.Unit.Table is not null))
            foreach (var (cell, bounds) in obj.Cells)
                if (cell.ContainsSource(sourceOffset))
                    return cell.CaretBounds(sourceOffset).Translate(new Vector(bounds.X + 8, TextBounds(obj.Offset).Y + bounds.Y + CellTop(cell, bounds)));
        var displayOffset = ToDisplayOffset(sourceOffset);
        var rect = TextBounds(displayOffset);
        var unit = _mapping.FirstOrDefault(m => displayOffset >= m.Start && displayOffset < m.Start + m.Length)?.Unit;
        // Newlines inserted by the projection use body styling. At a line end,
        // keep the preceding character's size without inheriting across blank lines.
        if (unit is null || unit.Text is "\n" or "\r\n")
        {
            var previous = _mapping.LastOrDefault(m => m.Length > 0 && m.Start + m.Length <= displayOffset)?.Unit;
            if (previous is not null && !previous.Text.Contains('\n') && !previous.Text.Contains('\r') &&
                previous.Table is null && previous.ImageUrl is null && previous.D2Code is null)
                unit = previous;
        }
        var height = Math.Max(1, unit?.Size ?? Typography.Body);
        return new Rect(rect.X, rect.Y + (rect.Height - height) / 2, 2, height);
    }

    public bool IsOnLastContentLine(int sourceOffset)
    {
        var last = _mapping.LastOrDefault(m => m.Length > 0 && (m.Unit.Text.Trim('\r', '\n').Length > 0 ||
            m.Unit.ImageUrl is not null || m.Unit.D2Code is not null || m.Unit.Table is not null));
        if (last is null) return true;
        return Layout.GetLineIndexFromCharacterIndex(ToDisplayOffset(sourceOffset), false) >=
            Layout.GetLineIndexFromCharacterIndex(last.Start + last.Length - 1, false);
    }

    public IReadOnlyList<Rect> SelectionBounds(int sourceStart, int sourceEnd)
    {
        var lo = Math.Min(sourceStart, sourceEnd);
        var hi = Math.Max(sourceStart, sourceEnd);
        var selected = _mapping.Where(m => m.Unit.Start < hi && m.Unit.End > lo).ToArray();
        var cellSelections = new List<Rect>();
        foreach (var obj in _objects.Where(o => o.Unit.Table is not null))
            foreach (var (cell, bounds) in obj.Cells)
                cellSelections.AddRange(cell.SelectionBounds(sourceStart, sourceEnd).Select(r =>
                    r.Translate(new Vector(bounds.X + 8, TextBounds(obj.Offset).Y + bounds.Y + CellTop(cell, bounds)))));
        var normal = selected.Where(m => m.Unit.Table is null).ToArray();
        if (normal.Length > 0)
            cellSelections.AddRange(TextRangeBounds(normal[0].Start,
                normal[^1].Start + normal[^1].Length - normal[0].Start));
        return cellSelections;
    }

    public DisplayUnit? InteractiveAt(Point point)
    {
        foreach (var obj in _objects)
        {
            var top = TextBounds(obj.Offset).Y;
            foreach (var (cell, bounds) in obj.Cells)
                if (bounds.Contains(new Point(point.X, point.Y - top)))
                    return cell.InteractiveAt(new Point(point.X - bounds.X - 8, point.Y - top - bounds.Y - CellTop(cell, bounds)));
        }
        foreach (var item in _mapping.Where(m => m.Unit.LinkUrl is not null || m.Unit.TaskOffset.HasValue))
            if (TextRangeBounds(item.Start, item.Length).Any(r => r.Contains(point))) return item.Unit;
        return null;
    }

    public int HitTest(Point point)
    {
        foreach (var obj in _objects)
        {
            var top = TextBounds(obj.Offset).Y;
            if (point.Y < top || point.Y >= top + obj.Height) continue;
            foreach (var (cell, bounds) in obj.Cells)
                if (bounds.Contains(new Point(point.X, point.Y - top)))
                    return cell.HitTestCell(new Point(point.X - bounds.X - 8, point.Y - top - bounds.Y - CellTop(cell, bounds)));
            return point.X < _width / 2 ? obj.Unit.Start : obj.Unit.End;
        }
        var hit = Layout.HitTestPoint(new Point(point.X, RawY(point.Y)));
        return ToSourceOffset(hit.TextPosition, hit.CharacterHit.TrailingLength > 0);
    }

    public int MoveVertical(int sourceOffset, int direction, double? desiredX = null)
    {
        var caret = CaretBounds(sourceOffset);
        if (desiredX is { } x) caret = new Rect(x, caret.Y, caret.Width, caret.Height);
        foreach (var obj in _objects.Where(o => o.Unit.Table is not null))
        {
            foreach (var (cell, bounds) in obj.Cells)
            {
                if (!cell.ContainsSource(sourceOffset)) continue;
                var moved = cell.MoveVertical(sourceOffset, direction, caret.X - bounds.X - 8);
                if (moved != sourceOffset) return moved;
                var adjacent = obj.Cells.Where(c => c.Rect.X == bounds.X &&
                    (direction > 0 ? c.Rect.Y > bounds.Y : c.Rect.Y < bounds.Y));
                var target = direction > 0 ? adjacent.FirstOrDefault() : adjacent.LastOrDefault();
                if (target.Layout is not null)
                    return target.Layout.HitTestCell(new Point(caret.X - target.Rect.X - 8,
                        direction > 0 ? target.Layout.Layout.TextLines[0].Height / 2 :
                            target.Layout.Height - target.Layout.Layout.TextLines[^1].Height / 2));
                var objectTop = TextBounds(obj.Offset).Y;
                return HitTest(new Point(caret.X, direction > 0 ? objectTop + obj.Height + 1 : objectTop - 1));
            }
        }
        var lines = Layout.TextLines;
        var currentIndex = Layout.GetLineIndexFromCharacterIndex(ToDisplayOffset(sourceOffset), false);
        var step = Math.Sign(direction);
        if (step == 0) return sourceOffset;
        // An image/diagram spans several display lines but maps to one source
        // range. Walk past lines that hit the same source position.
        for (var targetIndex = currentIndex + step; targetIndex >= 0 && targetIndex < lines.Count; targetIndex += step)
        {
            var top = _lineTops[targetIndex];
            var target = HitTest(new Point(caret.X, top + lines[targetIndex].Height / 2));
            // Enter a table at its first cell, independently of the incoming column.
            var table = _objects.FirstOrDefault(o => o.Unit.Table is not null &&
                target >= o.Unit.Start && target < o.Unit.End);
            if (table is not null)
                return table.Cells.SelectMany(c => c.Layout._mapping).FirstOrDefault()?.Unit.Start ?? table.Unit.Start;
            if (target != sourceOffset) return target;
        }
        return sourceOffset;
    }

    public void Dispose()
    {
        Layout.Dispose();
        foreach (var obj in _objects) { obj.Image?.Dispose(); foreach (var cell in obj.Cells) cell.Layout.Dispose(); }
    }
}

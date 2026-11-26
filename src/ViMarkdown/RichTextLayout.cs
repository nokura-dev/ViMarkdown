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
public sealed partial class RichTextLayout : IDisposable
{
    private record MappedUnit(DisplayUnit Unit, int Start, int Length);
    private readonly List<MappedUnit> _mapping = [];
    private sealed record ObjectLayout(DisplayUnit Unit, int Offset, double Height, Bitmap? Image, Size ImageSize,
        List<(RichTextLayout Layout, Rect Rect)> Cells, string? Error, bool Pending)
    {
        public double Top { get; set; }
    }
    private readonly List<ObjectLayout> _objects = [];
    private readonly double _width;
    private readonly IReadOnlyDictionary<int, double>? _imageWidths;
    private const double CellPaddingX = 8;
    private const double CellPaddingY = 4;
    private const double ImageLeftPadding = 4;
    public string Text { get; }
    public TextLayout Layout { get; }
    private readonly int[] _lineOffsets;
    private readonly double[] _lineTops;
    private readonly double[] _lineShifts;
    private readonly double[] _lineHeights;
    public double Height { get; }
    public Vector FirstLineInkInset { get; }

    public RichTextLayout(IReadOnlyList<DisplayUnit> units, double width, Func<string, byte[]?>? imageResolver = null, double lineHeight = double.NaN,
        Func<string, D2Result?>? d2Resolver = null, TextAlignment alignment = TextAlignment.Left,
        IReadOnlyDictionary<int, double>? imageWidths = null)
    {
        _width = Math.Max(1, width);
        _imageWidths = imageWidths;
        // Let each text line follow its font metrics (including headings and
        // fallback fonts). Object placeholders use the same body-line metrics.
        using var bodyLine = new TextLayout(" ", Typography.BodyTypeface, Typography.Body, WorkspaceColors.Text,
            lineHeight: lineHeight);
        var objectLineHeight = bodyLine.TextLines[0].Height;
        var text = new StringBuilder();
        var styles = new List<ValueSpan<TextRunProperties>>();
        var exactHeightBlocks = new List<(int Offset, int Length, double Height, bool TrimFollowingGap)>();
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
                var (obj, objectLines) = CreateObjectLayout(unit, text.Length, objectLineHeight, imageResolver, lineHeight, d2Resolver);
                if (obj.Error is not null)
                    exactHeightBlocks.Add((text.Length, objectLines, obj.Height, !obj.Pending));
                else if (unit.D2Code is not null && obj.Image is not null)
                    // Match D2 images to their displayed height without bottom
                    // padding or rounding up to a whole text line.
                    exactHeightBlocks.Add((text.Length, objectLines, obj.Height, false));
                _objects.Add(obj);
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
        _lineOffsets = lines.Select(line => line.FirstTextSourceIndex).ToArray();
        _lineHeights = lines.Select(line => line.Height).ToArray();
        foreach (var block in exactHeightBlocks)
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
        foreach (var obj in _objects)
            obj.Top = _lineTops[DisplayLine(obj.Offset)];
        FirstLineInkInset = TextInkAlignment.Inset(Layout.TextLines[0], Text);
    }

    private int DisplayLine(int offset)
    {
        var index = Array.BinarySearch(_lineOffsets, offset);
        return index >= 0 ? index : Math.Max(0, ~index - 1);
    }

    private static bool Visible(double top, double height, Rect? viewport)
        => viewport is not { } view || top <= view.Bottom && top + height >= view.Top;

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

    // All table drawing and pointer/caret mapping use the same cell origin.
    private static Vector CellOrigin(ObjectLayout table, RichTextLayout cell, Rect bounds)
        => new(bounds.X + CellPaddingX, table.Top + bounds.Y + (bounds.Height - cell.Height) / 2);

    private static bool SameStyle(DisplayUnit a, DisplayUnit b)
        => a.Size == b.Size && a.Bold == b.Bold && a.Italic == b.Italic && a.Code == b.Code && a.Strike == b.Strike && a.LinkUrl == b.LinkUrl
            && a.TaskOffset.HasValue == b.TaskOffset.HasValue;

    public void Dispose()
    {
        Layout.Dispose();
        foreach (var obj in _objects) { obj.Image?.Dispose(); foreach (var cell in obj.Cells) cell.Layout.Dispose(); }
    }
}

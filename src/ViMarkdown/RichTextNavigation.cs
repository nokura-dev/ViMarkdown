using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace ViMarkdown;

public sealed partial class RichTextLayout
{
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
                    return cell.CaretBounds(sourceOffset).Translate(CellOrigin(obj, cell, bounds));
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
                    r.Translate(CellOrigin(obj, cell, bounds))));
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
            var top = obj.Top;
            foreach (var (cell, bounds) in obj.Cells)
                if (bounds.Contains(new Point(point.X, point.Y - top)))
                    return cell.InteractiveAt(point - CellOrigin(obj, cell, bounds));
        }
        foreach (var item in _mapping.Where(m => m.Unit.LinkUrl is not null || m.Unit.TaskOffset.HasValue))
            if (TextRangeBounds(item.Start, item.Length).Any(r => r.Contains(point))) return item.Unit;
        return null;
    }

    public int HitTest(Point point)
    {
        foreach (var obj in _objects)
        {
            var top = obj.Top;
            if (point.Y < top || point.Y >= top + obj.Height) continue;
            foreach (var (cell, bounds) in obj.Cells)
                if (bounds.Contains(new Point(point.X, point.Y - top)))
                    return cell.HitTestCell(point - CellOrigin(obj, cell, bounds));
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
                var moved = cell.MoveVertical(sourceOffset, direction, caret.X - bounds.X - CellPaddingX);
                if (moved != sourceOffset) return moved;
                var adjacent = obj.Cells.Where(c => c.Rect.X == bounds.X &&
                    (direction > 0 ? c.Rect.Y > bounds.Y : c.Rect.Y < bounds.Y));
                var target = direction > 0 ? adjacent.FirstOrDefault() : adjacent.LastOrDefault();
                if (target.Layout is not null)
                    return target.Layout.HitTestCell(new Point(caret.X - target.Rect.X - CellPaddingX,
                        direction > 0 ? target.Layout.Layout.TextLines[0].Height / 2 :
                            target.Layout.Height - target.Layout.Layout.TextLines[^1].Height / 2));
                var objectTop = obj.Top;
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
}

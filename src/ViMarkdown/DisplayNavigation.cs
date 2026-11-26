namespace ViMarkdown;

// Keep source offsets for edits, but expose only projected glyphs to navigation.
internal sealed class DisplayNavigation
{
    private sealed record Glyph(string Text, int Offset, int Line, bool CanStop);
    private readonly Glyph[] _glyphs;
    private readonly int[] _lineStarts;
    private readonly int _sourceLength;
    private readonly HashSet<int> _carriageReturns;

    public DisplayNavigation(string source, IReadOnlyList<DisplayUnit> units)
    {
        _sourceLength = source.Length;
        _carriageReturns = Enumerable.Range(0, source.Length).Where(i => source[i] == '\r').ToHashSet();
        _lineStarts = new[] { 0 }.Concat(Enumerable.Range(0, source.Length)
            .Where(i => source[i] == '\n').Select(i => i + 1)).ToArray();
        var glyphs = new List<Glyph>();
        void Add(IEnumerable<DisplayUnit> items)
        {
            foreach (var unit in items)
            {
                if (unit.Table is { } rows)
                {
                    foreach (var row in rows)
                    foreach (var cell in row)
                    {
                        Add(cell);
                        var end = cell.LastOrDefault()?.End ?? unit.Start;
                        glyphs.Add(new(" ", end, Line(end), true));
                    }
                    continue;
                }
                var line = Line(unit.Start);
                var newline = unit.Text is "\n" or "\r\n";
                var blank = newline && unit.Start < source.Length &&
                    source.AsSpan(_lineStarts[line], unit.Start - _lineStarts[line]).Trim().IsEmpty;
                if (unit.End == unit.Start && !blank && unit.Text != " ") continue; // display-only indentation/spacers
                if (newline)
                    glyphs.Add(new("\n", unit.Start, line, blank));
                else
                    glyphs.Add(new(unit.Rule || unit.ImageUrl is not null || unit.D2Code is not null ? "\uFFFC" : unit.Text,
                        unit.Start, line, true));
            }
        }
        Add(units);
        if (source.EndsWith('\n') && glyphs.All(g => !g.CanStop || g.Offset != source.Length))
            glyphs.Add(new("\n", source.Length, Line(source.Length), true));
        _glyphs = glyphs.ToArray();
    }

    private int Line(int offset)
    {
        var index = Array.BinarySearch(_lineStarts, offset);
        return index >= 0 ? index : Math.Max(0, ~index - 1);
    }

    public int Normalize(int offset)
    {
        var stops = _glyphs.Where(g => g.CanStop).ToArray();
        if (stops.Any(g => g.Offset == offset)) return offset;
        var sameLine = stops.Where(g => g.Line == Line(offset)).ToArray();
        var candidates = sameLine.Length > 0 ? sameLine : stops;
        return candidates.FirstOrDefault(g => g.Offset >= offset)?.Offset
            ?? candidates.LastOrDefault()?.Offset ?? 0;
    }

    public int AtOrAfter(int offset)
        => _glyphs.Where(g => g.CanStop && g.Offset >= offset)
            .Select(g => g.Offset).DefaultIfEmpty(_sourceLength).Min();

    public int Horizontal(int offset, int direction, int count)
    {
        offset = Normalize(offset);
        var stops = _glyphs.Where(g => g.CanStop && g.Line == Line(offset)).ToArray();
        var index = Array.FindIndex(stops, g => g.Offset == offset);
        return index < 0 ? offset : stops[Math.Clamp(index + direction * count, 0, stops.Length - 1)].Offset;
    }

    public int PreviousInLine(int offset)
    {
        var line = Line(offset);
        // Projection order may include synthetic separators pointing back to the
        // start of a source line. Choose the closest source offset, not the last unit.
        return _glyphs.Where(g => g.CanStop && g.Line == line && g.Offset < offset)
            .Select(g => g.Offset).DefaultIfEmpty(offset).Max();
    }

    public int CaretHorizontal(int offset, int direction)
    {
        var stops = _glyphs.Where(g => g.CanStop).Select(g => g.Offset).ToList();
        foreach (var line in _glyphs.Where(g => g.CanStop).Select(g => g.Line).Distinct())
        {
            var end = line + 1 < _lineStarts.Length ? _lineStarts[line + 1] - 1 : _sourceLength;
            if (end > _lineStarts[line] && _carriageReturns.Contains(end - 1)) end--;
            stops.Add(end);
        }
        return direction < 0 ? stops.Where(p => p < offset).DefaultIfEmpty(offset).Max()
            : stops.Where(p => p > offset).DefaultIfEmpty(offset).Min();
    }

    public int Boundary(int offset, bool end, bool nonWhitespace = false)
    {
        offset = Normalize(offset);
        var stops = _glyphs.Where(g => g.CanStop && g.Line == Line(offset)).ToArray();
        return (end ? stops.LastOrDefault() : nonWhitespace
            ? stops.FirstOrDefault(g => !string.IsNullOrWhiteSpace(g.Text)) ?? stops.FirstOrDefault()
            : stops.FirstOrDefault())?.Offset ?? offset;
    }

    public int Word(int offset, char motion, int count)
    {
        offset = Normalize(offset);
        var index = Array.FindIndex(_glyphs, g => g.CanStop && g.Offset == offset);
        if (index < 0) return offset;
        var big = char.IsUpper(motion);
        int Kind(int i) => string.IsNullOrWhiteSpace(_glyphs[i].Text) ? 0
            : big || char.IsLetterOrDigit(_glyphs[i].Text, 0) || _glyphs[i].Text[0] == '_' ? 1 : 2;
        while (count-- > 0)
        {
            if (motion is 'b' or 'B')
            {
                index = Math.Max(0, index - 1);
                while (index > 0 && Kind(index) == 0) index--;
                var kind = Kind(index);
                while (index > 0 && Kind(index - 1) == kind) index--;
            }
            else if (motion is 'e' or 'E')
            {
                index = Math.Min(_glyphs.Length - 1, index + 1);
                while (index < _glyphs.Length - 1 && Kind(index) == 0) index++;
                var kind = Kind(index);
                while (index < _glyphs.Length - 1 && Kind(index + 1) == kind) index++;
            }
            else
            {
                var kind = Kind(index);
                while (index < _glyphs.Length - 1 && Kind(index) == kind) index++;
                while (index < _glyphs.Length - 1 && Kind(index) == 0) index++;
            }
        }
        return Normalize(_glyphs[index].Offset);
    }
}

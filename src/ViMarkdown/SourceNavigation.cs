using AvaloniaEdit.Document;

namespace ViMarkdown;

// Raw Markdown positions for operators and text objects. Display motions stay in the window.
internal sealed class SourceNavigation(TextDocument document)
{
    public int Next(int p) => TextBoundaries.Next(document.Text, p);
    public int Previous(int p) => TextBoundaries.Previous(document.Text, p);
    public DocumentLine LineAt(int p) => document.GetLineByOffset(Math.Clamp(p, 0, document.TextLength));
    public int FirstNonWhitespace(int p)
    {
        var line = LineAt(p); var n = line.Offset;
        while (n < line.EndOffset && char.IsWhiteSpace(document.GetCharAt(n))) n = Next(n);
        return n;
    }
    public int LastCharacter(int p) => LineAt(p).Length == 0 ? LineAt(p).Offset : Previous(LineAt(p).EndOffset);
    public int GraphemeColumn(int p)
    {
        var col = 0;
        for (var i = LineAt(p).Offset; i < p; i = Next(i)) col++;
        return col;
    }
    public int AtGraphemeColumn(DocumentLine line, int column)
    {
        var p = line.Offset;
        while (column-- > 0 && p < line.EndOffset) p = Next(p);
        return p;
    }
    public string PreferredNewLine()
    {
        for (var n = 1; n <= document.LineCount; n++)
        {
            var line = document.GetLineByNumber(n);
            if (line.DelimiterLength > 0) return document.GetText(line.EndOffset, line.DelimiterLength);
        }
        return "\n";
    }
    public int AdvanceInLine(int p, int count)
    {
        var end = LineAt(p).EndOffset;
        while (count-- > 0 && p < end) p = Next(p);
        return p;
    }
    private int WordClass(int p, bool big)
    {
        if (p >= document.TextLength) return -1;
        var c = document.GetCharAt(p);
        return char.IsWhiteSpace(c) ? 0 : big || char.IsLetterOrDigit(c) || c == '_' ? 1 : 2;
    }
    public int WordForward(int p, int count, bool big)
    {
        while (count-- > 0 && p < document.TextLength)
        {
            var kind = WordClass(p, big);
            while (p < document.TextLength && WordClass(p, big) == kind) p = Next(p);
            while (p < document.TextLength && WordClass(p, big) == 0) p = Next(p);
        }
        return p;
    }
    public int WordBackward(int p, int count, bool big)
    {
        while (count-- > 0 && p > 0)
        {
            p = Previous(p);
            while (p > 0 && WordClass(p, big) == 0) p = Previous(p);
            var kind = WordClass(p, big);
            while (p > 0 && WordClass(Previous(p), big) == kind) p = Previous(p);
        }
        return p;
    }
    public int ChangeWordEnd(int position, int count, bool big)
    {
        for (var n = 0; n < count; n++)
        {
            if (n > 0) position = WordForward(position, 1, big);
            var kind = WordClass(position, big);
            while (Next(position) < document.TextLength && WordClass(Next(position), big) == kind)
                position = Next(position);
        }
        return position;
    }
    public int WordEnd(int p, int count, bool big)
    {
        while (count-- > 0 && p < document.TextLength)
        {
            p = Next(p);
            while (p < document.TextLength && WordClass(p, big) == 0) p = Next(p);
            var kind = WordClass(p, big);
            while (p < document.TextLength && Next(p) < document.TextLength && WordClass(Next(p), big) == kind) p = Next(p);
        }
        return p;
    }
    public IReadOnlyList<(int Start, int End)> LineRange(int p, int count)
        => LinesBetween(p, document.GetLineByNumber(Math.Min(document.LineCount, LineAt(p).LineNumber + count - 1)).Offset);
    public IReadOnlyList<(int Start, int End)> LinesBetween(int a, int b)
    {
        var first = LineAt(Math.Min(a, b)); var last = LineAt(Math.Max(a, b));
        return new[] { (first.Offset, last.Offset + last.TotalLength) };
    }
    public (int Start, int End)? FindTextObject(int caret, char obj, bool around, int count)
    {
        var text = document.Text; if (text.Length == 0) return null;
        var p = Math.Min(caret, text.Length - 1);
        if (obj is 'w' or 'W')
        {
            var big = obj == 'W'; var start = p; var kind = WordClass(p, big);
            while (start > 0 && WordClass(Previous(start), big) == kind) start = Previous(start);
            var end = p;
            while (end < text.Length && WordClass(end, big) == kind) end = Next(end);
            for (var n = 1; n < count; n++) { while (end < text.Length && WordClass(end, big) == 0) end = Next(end); var c = WordClass(end, big); while (end < text.Length && WordClass(end, big) == c) end = Next(end); }
            if (around)
            {
                var original = end;
                while (end < text.Length && char.IsWhiteSpace(text[end])) end = Next(end);
                if (end == original) while (start > 0 && char.IsWhiteSpace(text[Previous(start)])) start = Previous(start);
            }
            return (start, end);
        }
        if (obj is 'p' or 's')
        {
            var start = LineAt(p); var end = start;
            while (start.PreviousLine is { Length: > 0 } prev) start = prev;
            while (end.NextLine is { Length: > 0 } next) end = next;
            return (start.Offset, around ? end.Offset + end.TotalLength : end.EndOffset);
        }
        char open, close;
        switch (obj)
        {
            case '(': case ')': case 'b': open = '('; close = ')'; break;
            case '[': case ']': open = '['; close = ']'; break;
            case '{': case '}': case 'B': open = '{'; close = '}'; break;
            case '<': case '>': open = '<'; close = '>'; break;
            case '\'': case '"': case '`': open = close = obj; break;
            default: return null;
        }
        if (open == close)
        {
            var bounds = LineAt(p); var left = -1;
            for (var j = bounds.Offset; j < bounds.EndOffset; j++)
            {
                if (text[j] != open || (j > 0 && text[j - 1] == '\\')) continue;
                if (left < 0) left = j;
                else { if (p <= j) return (around ? left : left + 1, around ? j + 1 : j); left = -1; }
            }
            return null;
        }
        var stack = new Stack<int>(); var pairs = new List<(int Start, int End)>();
        for (var j = 0; j < text.Length; j++)
        {
            if (text[j] == open) stack.Push(j);
            if (text[j] == close && stack.Count > 0)
            { var left = stack.Pop(); if (left <= p && p <= j) pairs.Add((left, j)); }
        }
        var pair = pairs.OrderBy(x => x.End - x.Start).Skip(count - 1).FirstOrDefault((Start: -1, End: -1));
        return pair.Start < 0 ? null : (around ? pair.Start : pair.Start + 1, around ? pair.End + 1 : pair.End);
    }
}

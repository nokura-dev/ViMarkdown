using AvaloniaEdit.Document;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private bool _replaceMode;
    private bool _continueInsertUndo;
    private bool _insertInputGroupOpen;
    private TextAnchor? _blockInsertStart;
    private List<TextAnchor>? _blockInsertTargets;

    private void Operate(char op, IReadOnlyList<(int Start, int End)> ranges, int kind)
    {
        _verticalColumn = null;
        var copy = ranges.ToArray(); if (copy.Length == 0) return;
        if (op is 'd' or 'c' && kind == 0 && copy.Length == 1 && copy[0].End == _source.Next(copy[0].Start))
        {
            bool ContainsImage(DisplayUnit unit) => unit.ImageUrl is not null && copy[0].Start >= unit.Start && copy[0].Start < unit.End
                || unit.Table?.SelectMany(row => row).SelectMany(cell => cell).Any(ContainsImage) == true;
            if (Projection.Build(_pageContent.Text).Any(ContainsImage)) { copy = _source.LineRange(copy[0].Start, 1).ToArray(); kind = 1; }
        }
        var start = copy[0].Start;
        var blockPositions = kind == 2 ? copy.Select(r => (Line: _source.LineAt(r.Start).LineNumber, Column: _source.GraphemeColumn(r.Start))).ToArray() : null;
        if (op is '>' or '<' or '=') { Indent(op, _source.LineAt(start).LineNumber, _source.LineAt(Math.Max(start, copy[^1].End - 1)).LineNumber); return; }
        _register = string.Join(kind == 2 ? "\n" : "", copy.Select(r => _pageContent.GetText(r.Start, r.End - r.Start)));
        if (kind == 1 && !_register.EndsWith('\n')) _register += _source.PreferredNewLine();
        if (op is 'y' or 'd' && copy.Any(r => r.End > r.Start))
            _vimClipboardWrite = WriteVimClipboardAsync(_vimClipboardWrite, _register, kind);
        if (op == 'y')
        {
            var target = kind == 1 && _mode != EditorMode.Visual && start == _source.LineAt(_caret).Offset ? _caret : start;
            SwitchMode(EditorMode.Normal); _caret = target; return;
        }
        (int Anchor, int Caret, int Kind)? previousVisual = _mode == EditorMode.Visual && _anchor is { } anchor
            ? (anchor, _caret, _visualKind) : null;
        var changed = false;
        _editor.Select(start, 0);
        using (_pageContent.RunUpdate())
        {
            foreach (var range in copy.Reverse())
            {
                var from = range.Start; var end = range.End;
                if (kind == 1 && op == 'c') end = _source.LineAt(Math.Max(from, end - 1)).EndOffset;
                else if (kind == 1 && end == _pageContent.TextLength && _source.LineAt(from).PreviousLine is { } previous && _source.LineAt(Math.Max(from, end - 1)).NextLine is null)
                    from = previous.EndOffset;
                _editor.Select(from, 0);
                if (end > from) { _pageContent.Remove(from, end - from); changed = true; }
            }
        }
        _caret = kind == 1 && op == 'd' ? _source.FirstNonWhitespace(Math.Min(start, _pageContent.TextLength)) : Math.Min(start, _pageContent.TextLength);
        _continueInsertUndo = op == 'c' && changed;
        SwitchMode(op == 'c' ? EditorMode.Insert : EditorMode.Normal);
        if (previousVisual is not null) _lastVisual = previousVisual;
        if (op == 'c' && blockPositions is not null)
            BeginBlockInsert(blockPositions.Select(p => _source.AtGraphemeColumn(_pageContent.GetLineByNumber(p.Line), p.Column)).ToArray());
    }
    private void BeginBlockInsert(IReadOnlyList<int> positions)
    {
        _caret = positions[0];
        SwitchMode(EditorMode.Insert);
        _blockInsertStart = _pageContent.CreateAnchor(positions[0]);
        _blockInsertStart.MovementType = AnchorMovementType.BeforeInsertion;
        _blockInsertTargets = positions.Skip(1).Select(p =>
        {
            var anchor = _pageContent.CreateAnchor(p);
            anchor.MovementType = AnchorMovementType.BeforeInsertion;
            return anchor;
        }).ToList();
    }
    private void FinishBlockInsert()
    {
        var start = _blockInsertStart; var targets = _blockInsertTargets;
        _blockInsertStart = null; _blockInsertTargets = null;
        if (start is null || start.IsDeleted || targets is null || _editor.CaretOffset < start.Offset) return;
        var text = _pageContent.GetText(start.Offset, _editor.CaretOffset - start.Offset);
        if (text.Length == 0 || text.Contains('\n')) return;
        _pageContent.UndoStack.StartContinuedUndoGroup();
        try
        {
            foreach (var target in targets.AsEnumerable().Reverse())
                if (!target.IsDeleted) _pageContent.Insert(target.Offset, text);
        }
        finally { _pageContent.UndoStack.EndUndoGroup(); }
    }
    private void Paste(bool after, int count, string text, int kind)
    {
        if (text.Length == 0) return;
        var visualPaste = _mode == EditorMode.Visual;
        var visualStart = visualPaste ? VimSelection()[0].Start : _caret;
        var visualLine = visualPaste && _visualKind == 1;
        if (visualLine && kind == 0)
        {
            kind = 1;
            if (!text.EndsWith('\n')) text += _source.PreferredNewLine();
        }
        _editor.Select(visualStart, 0);
        using (_pageContent.RunUpdate())
        {
            if (visualPaste)
            {
                Operate('d', VimSelection(), _visualKind);
                after = visualLine && visualStart > _pageContent.TextLength;
                if (after) _caret = _pageContent.TextLength;
            }
            if (kind == 2)
            {
                var rows = text.Split('\n'); var number = _source.LineAt(_caret).LineNumber; var column = _source.GraphemeColumn(_caret) + (after ? 1 : 0);
                for (var n = 0; n < rows.Length; n++)
                {
                    while (number + n > _pageContent.LineCount) _pageContent.Insert(_pageContent.TextLength, "\n");
                    var line = _pageContent.GetLineByNumber(number + n); var p = _source.AtGraphemeColumn(line, column);
                    _pageContent.Insert(p, new string(' ', Math.Max(0, column - _source.GraphemeColumn(p))) + string.Concat(Enumerable.Repeat(rows[n], count)));
                }
            }
            else
            {
                var p = kind == 1 ? (after ? _source.LineAt(_caret).Offset + _source.LineAt(_caret).TotalLength : _source.LineAt(_caret).Offset) : (after ? Math.Min(_source.Next(_caret), _source.LineAt(_caret).EndOffset) : _caret);
                text = string.Concat(Enumerable.Repeat(text, count));
                if (kind == 1 && after && _source.LineAt(_caret).DelimiterLength == 0)
                {
                    var trailing = text.EndsWith("\r\n", StringComparison.Ordinal) ? 2 : 1;
                    text = _source.PreferredNewLine() + text[..^trailing]; p = _pageContent.TextLength;
                }
                _editor.Select(p, 0); _pageContent.Insert(p, text); _caret = kind == 1 ? _source.FirstNonWhitespace(p + (text.StartsWith("\r\n", StringComparison.Ordinal) ? 2 : text.StartsWith('\n') ? 1 : 0)) : _source.Previous(p + text.Length);
            }
        }
        SwitchMode(EditorMode.Normal);
    }
    private void JoinLines(int count)
    {
        var start = _mode == EditorMode.Visual ? Math.Min(_anchor ?? _caret, _caret) : _caret;
        var number = _source.LineAt(start).LineNumber;
        _editor.Select(_source.LineAt(start).EndOffset, 0);
        using (_pageContent.RunUpdate())
            while (--count > 0)
            {
                var line = _pageContent.GetLineByNumber(number); if (line.NextLine is not { } next) break;
                var end = _source.FirstNonWhitespace(next.Offset); var space = line.Length > 0 && !char.IsWhiteSpace(_pageContent.GetCharAt(line.EndOffset - 1)) && end < next.EndOffset && _pageContent.GetCharAt(end) != ')' ? " " : "";
                _caret = line.EndOffset; _editor.Select(_caret, 0); _pageContent.Replace(line.EndOffset, end - line.EndOffset, space);
            }
        SwitchMode(EditorMode.Normal);
    }
    private void Indent(char op, int first, int last)
    {
        _editor.Select(_pageContent.GetLineByNumber(first).Offset, 0);
        using (_pageContent.RunUpdate())
            for (var n = first; n <= last; n++)
            {
                var line = _pageContent.GetLineByNumber(n); var end = _source.FirstNonWhitespace(line.Offset);
                var whitespace = _pageContent.GetText(line.Offset, end - line.Offset);
                var replacement = op == '>' ? "    " + whitespace : op == '<' ? whitespace[(whitespace.StartsWith('\t') ? 1 : Math.Min(4, whitespace.TakeWhile(c => c == ' ').Count()))..] : n > 1 ? new string(' ', _source.FirstNonWhitespace(_pageContent.GetLineByNumber(n - 1).Offset) - _pageContent.GetLineByNumber(n - 1).Offset) : "";
                _editor.Select(line.Offset, 0); _pageContent.Replace(line.Offset, whitespace.Length, replacement);
            }
        _caret = _source.FirstNonWhitespace(_pageContent.GetLineByNumber(first).Offset); SwitchMode(EditorMode.Normal);
    }
}

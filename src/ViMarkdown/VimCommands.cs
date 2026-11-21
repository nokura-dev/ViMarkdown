using System.Text.RegularExpressions;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private static int ReadCount(string s, ref int i)
    {
        long n = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i]) && (s[i] != '0' || n > 0))
            n = Math.Min(100000, n * 10 + s[i++] - '0');
        return (int)Math.Max(1, n);
    }
    private async Task<bool> ExecuteVimAsync(string input)
    {
        var i = 0; var count = ReadCount(input, ref i); var hasCount = i > 0;
        if (i == input.Length) return false;
        var command = input[i++];
        if (command is ':' or '/' or '?')
        {
            _searchOrigin = _caret;
            _commandPrompt = command.ToString(); _promptOpeningText = _commandPrompt;
            if (command == ':' && _mode == EditorMode.Visual) { _commandPrompt += "'<,'>"; SwitchMode(EditorMode.Normal); }
            return true;
        }
        if (_mode == EditorMode.Visual && command is 'o' or 'O')
        {
            var anchor = _anchor ?? _caret;
            if (_visualKind == 2 && command == 'O')
            {
                _anchor = _source.AtGraphemeColumn(_source.LineAt(anchor), _source.GraphemeColumn(_caret));
                _caret = _source.AtGraphemeColumn(_source.LineAt(_caret), _source.GraphemeColumn(anchor));
            }
            else { _anchor = _caret; _caret = anchor; }
            return true;
        }
        if (command == 'g')
        {
            if (i == input.Length) return false;
            if (input[i] == 'g') _caret = _source.FirstNonWhitespace(_pageContent.GetLineByNumber(Math.Min(count, _pageContent.LineCount)).Offset);
            else if (input[i] == 'v' && _lastVisual is { } last)
            {
                SwitchMode(EditorMode.Visual); _anchor = Math.Min(last.Anchor, _pageContent.TextLength);
                _caret = Math.Min(last.Caret, _pageContent.TextLength); _visualKind = last.Kind;
            }
            return true;
        }
        if (command == 'r')
        {
            if (i == input.Length) return false;
            if (_mode != EditorMode.Visual)
            {
                var available = 0;
                for (var p = _caret; p < _source.LineAt(_caret).EndOffset && available < count; p = _source.Next(p)) available++;
                if (available < count) return true;
            }
            var ranges = _mode == EditorMode.Visual ? VimSelection() : new[] { (_caret, _source.AdvanceInLine(_caret, count)) };
            var glyph = input[i..];
            var replacementCaret = _mode == EditorMode.Visual ? ranges[0].Item1 : _caret + glyph.Length * (count - 1);
            _editor.Select(ranges[0].Item1, 0);
            using (_pageContent.RunUpdate())
                foreach (var (start, end) in ranges.Reverse())
                {
                    var replacement = new System.Text.StringBuilder();
                    for (var p = start; p < end; p = _source.Next(p))
                        replacement.Append(_pageContent.GetCharAt(p) is '\r' or '\n' ? _pageContent.GetText(p, _source.Next(p) - p) : glyph);
                    _editor.Select(start, 0); _pageContent.Replace(start, end - start, replacement.ToString());
                }
            _caret = replacementCaret;
            SwitchMode(EditorMode.Normal); return true;
        }
        if (command is 'd' or 'c' or 'y' or '>' or '<' or '=')
        {
            if (_mode == EditorMode.Visual) { Operate(command, VimSelection(), _visualKind); return true; }
            var motionCount = ReadCount(input, ref i);
            count = (int)Math.Min(100000, (long)count * motionCount);
            if (i == input.Length) return false;
            if (input[i] == command)
            {
                Operate(command, _source.LineRange(_caret, count), 1); return true;
            }
            if (input[i] is 'i' or 'a')
            {
                if (i + 1 == input.Length) return false;
                var range = _source.FindTextObject(_caret, input[i + 1], input[i] == 'a', count);
                if (range is { } r) Operate(command, new[] { r }, 0);
                return true;
            }
            var motion = input[i..];
            if (motion == "g") return false;
            if (TryMotion(motion, count, hasCount || motionCount > 1, out var target, out var inclusive, out var linewise, true))
            {
                if (linewise) Operate(command, _source.LinesBetween(_caret, target), 1);
                else
                {
                    // cw changes to the word end, unlike dw which includes following space.
                    if (command == 'c' && motion is "w" or "W" && _caret < _pageContent.TextLength && !char.IsWhiteSpace(_pageContent.GetCharAt(_caret)))
                    {
                        target = _source.ChangeWordEnd(_caret, count, motion == "W");
                        inclusive = true;
                    }
                    var start = Math.Min(_caret, target); var end = Math.Max(_caret, target);
                    if (inclusive && _source.LineAt(end).Length > 0) end = _source.Next(end);
                    if (motion is "w" or "W" && _source.LineAt(end).LineNumber > _source.LineAt(start).LineNumber && end == _source.FirstNonWhitespace(end))
                        end = _pageContent.GetLineByNumber(_source.LineAt(end).LineNumber - 1).EndOffset;
                    Operate(command, new[] { (start, end) }, 0);
                }
            }
            return true;
        }
        if (_mode == EditorMode.Visual && command is 'i' or 'a')
        {
            if (i == input.Length) return false;
            var range = _source.FindTextObject(_caret, input[i], command == 'a', count);
            if (range is { } r) { _anchor = r.Start; _caret = _source.Previous(r.End); _visualKind = 0; }
            return true;
        }
        if (TryMotion(command.ToString(), count, hasCount, out var destination, out _, out _))
        { _caret = destination; return true; }
        switch (command)
        {
            case 'v': StartVisual(0); break;
            case 'V': StartVisual(1); break;
            case 'i': SwitchMode(EditorMode.Insert); break;
            case 'a': _caret = Math.Min(_source.Next(_caret), _source.LineAt(_caret).EndOffset); SwitchMode(EditorMode.Insert); break;
            case 'I':
            case 'A':
                if (_mode == EditorMode.Visual && _visualKind == 2)
                    BeginBlockInsert(VimSelection().Select(r => command == 'A' ? r.End : r.Start).ToArray());
                else { _caret = command == 'I' ? _source.FirstNonWhitespace(_caret) : _source.LineAt(_caret).EndOffset; SwitchMode(EditorMode.Insert); }
                break;
            case 'o':
            case 'O':
                var line = _source.LineAt(_caret); var indent = _pageContent.GetText(line.Offset, _source.FirstNonWhitespace(_caret) - line.Offset);
                var pos = command == 'o' ? line.EndOffset : line.Offset;
                var newline = _source.PreferredNewLine();
                var text = command == 'o' ? newline + indent : indent + newline;
                _editor.Select(pos, 0); _pageContent.Insert(pos, text);
                _caret = command == 'o' ? pos + text.Length : pos + indent.Length;
                _continueInsertUndo = true; SwitchMode(EditorMode.Insert); break;
            case 'p': case 'P': await PasteVimClipboardAsync(command == 'p', count); break;
            case 'x':
            case 's':
                if (_mode == EditorMode.Visual) Operate(command == 's' ? 'c' : 'd', VimSelection(), _visualKind);
                else Operate(command == 's' ? 'c' : 'd', new[] { (_caret, _source.AdvanceInLine(_caret, count)) }, 0);
                break;
            case 'D':
            case 'C':
                var endLine = _pageContent.GetLineByNumber(Math.Min(_pageContent.LineCount, _source.LineAt(_caret).LineNumber + count - 1));
                Operate(command == 'D' ? 'd' : 'c', new[] { (_caret, endLine.EndOffset) }, 0); break;
            case 'S': Operate('c', _source.LineRange(_caret, count), 1); break;
            case 'Y': Operate('y', _source.LineRange(_caret, count), 1); break;
            case 'R': SwitchMode(EditorMode.Insert); _replaceMode = true; break;
            case 'u': for (var n = 0; n < count; n++) ApplyHistory(false); break;
            case 'n': FindMatch(_searchBackward, count); break;
            case 'N': FindMatch(!_searchBackward, count); break;
            case '*':
            case '#':
                var word = _source.FindTextObject(_caret, 'w', false, 1);
                if (word is { } w)
                {
                    _searchPattern = @"\b" + Regex.Escape(_pageContent.GetText(w.Start, w.End - w.Start)) + @"\b";
                    _searchBackward = command == '#'; FindMatch(_searchBackward, count);
                }
                break;
            case 'J': JoinLines(_mode == EditorMode.Visual ? Math.Abs(_source.LineAt(_caret).LineNumber - _source.LineAt(_anchor ?? _caret).LineNumber) + 1 : Math.Max(2, count)); break;
        }
        return true;
    }
}

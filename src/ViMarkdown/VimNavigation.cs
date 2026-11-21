
namespace ViMarkdown;

public sealed partial class MainWindow
{
    private int _visualKind; // 0: character, 1: line, 2: block
    private (int Anchor, int Caret, int Kind)? _lastVisual;
    private int? _verticalColumn;
    private double? _verticalDisplayX;

    private void ClampNormal(bool allowLineEnd = false)
    {
        _caret = Math.Clamp(_caret, 0, _pageContent.TextLength);
        if (!allowLineEnd && _mode == EditorMode.Normal && _source.LineAt(_caret).Length > 0 && _caret >= _source.LineAt(_caret).EndOffset)
            _caret = _source.LastCharacter(_caret);
    }
    private IReadOnlyList<(int Start, int End)> VimSelection()
    {
        var a = Math.Clamp(_anchor ?? _caret, 0, _pageContent.TextLength);
        var b = Math.Clamp(_caret, 0, _pageContent.TextLength);
        if (_visualKind == 1)
            return new[] { (_source.LineAt(Math.Min(a, b)).Offset, _source.LineAt(Math.Max(a, b)).Offset + _source.LineAt(Math.Max(a, b)).TotalLength) };
        if (_visualKind == 2)
        {
            var result = new List<(int, int)>();
            var ca = _source.GraphemeColumn(a); var cb = _source.GraphemeColumn(b);
            for (var n = Math.Min(_source.LineAt(a).LineNumber, _source.LineAt(b).LineNumber); n <= Math.Max(_source.LineAt(a).LineNumber, _source.LineAt(b).LineNumber); n++)
            {
                var line = _pageContent.GetLineByNumber(n);
                var start = _source.AtGraphemeColumn(line, Math.Min(ca, cb)); var end = _source.AtGraphemeColumn(line, Math.Max(ca, cb));
                result.Add((start, end < line.EndOffset ? _source.Next(end) : end));
            }
            return result;
        }
        return new[] { (Math.Min(a, b), _source.Next(Math.Max(a, b))) };
    }
    private void StartVisual(int kind)
    {
        if (_mode == EditorMode.Visual && _visualKind == kind) { SwitchMode(EditorMode.Normal); return; }
        if (_mode != EditorMode.Visual) SwitchMode(EditorMode.Visual);
        _visualKind = kind;
    }
    private bool TryMotion(string motion, int count, bool hasCount, out int target, out bool inclusive, out bool linewise, bool operation = false)
    {
        if (motion == "^") motion = "0";
        target = _caret; inclusive = false; linewise = false;
        if (motion is not "j" and not "k") { _verticalColumn = null; _verticalDisplayX = null; }
        if (!operation && motion is "h" or "j" or "k" or "l" or "0" or "^" or "$"
            or "w" or "W" or "b" or "B" or "e" or "E")
        {
            if (motion is "h" or "l")
            {
                for (var n = 0; n < count; n++)
                {
                    target = _rich.MoveCaretHorizontal(target, motion == "h" ? -1 : 1);
                }
                return true;
            }
            if (motion == "$" && count > 1)
                target = _pageContent.GetLineByNumber(Math.Min(_pageContent.LineCount, _source.LineAt(target).LineNumber + count - 1)).Offset;
            if (motion is "j" or "k") _verticalDisplayX ??= _rich.NavigationX(target);
            target = _rich.MoveDisplayed(target, motion, count, _verticalDisplayX);
            if (motion == "$") target = _source.LineAt(target).EndOffset;
            inclusive = motion is "$" or "e" or "E";
            return true;
        }
        switch (motion)
        {
            case "h": for (var n = 0; n < count; n++) target = Math.Max(_source.LineAt(target).Offset, _source.Previous(target)); break;
            case "l": target = _source.AdvanceInLine(target, count); if (!operation && _mode != EditorMode.Visual) target = Math.Min(target, _source.LastCharacter(target)); break;
            case "j":
            case "k":
                var line = _pageContent.GetLineByNumber(Math.Clamp(_source.LineAt(target).LineNumber + (motion == "j" ? count : -count), 1, _pageContent.LineCount));
                _verticalColumn ??= _source.GraphemeColumn(target);
                target = Math.Min(_source.AtGraphemeColumn(line, _verticalColumn.Value), _source.LastCharacter(line.Offset)); linewise = true; break;
            case "w": case "W": target = _source.WordForward(target, count, motion == "W"); break;
            case "b": case "B": target = _source.WordBackward(target, count, motion == "B"); break;
            case "e": case "E": target = _source.WordEnd(target, count, motion == "E"); inclusive = true; break;
            case "0": target = _source.LineAt(target).Offset; break;
            case "$":
                var endLine = _pageContent.GetLineByNumber(Math.Min(_pageContent.LineCount, _source.LineAt(target).LineNumber + count - 1));
                target = _mode == EditorMode.Visual && !operation ? endLine.EndOffset : _source.LastCharacter(endLine.Offset);
                inclusive = true; break;
            case "gg":
            case "G":
                target = _source.FirstNonWhitespace(_pageContent.GetLineByNumber(motion == "gg" || hasCount ? Math.Min(count, _pageContent.LineCount) : _pageContent.LineCount).Offset); linewise = true; break;
            case "{":
            case "}":
                var number = _source.LineAt(target).LineNumber; var direction = motion == "}" ? 1 : -1;
                while (count-- > 0)
                {
                    if (_pageContent.GetLineByNumber(number).Length == 0)
                        while (number + direction >= 1 && number + direction <= _pageContent.LineCount &&
                            _pageContent.GetLineByNumber(number).Length == 0) number += direction;
                    else number = Math.Clamp(number + direction, 1, _pageContent.LineCount);
                    while (number > 1 && number < _pageContent.LineCount && _pageContent.GetLineByNumber(number).Length != 0) number += direction;
                }
                target = _pageContent.GetLineByNumber(number).Offset; break;
            default: return false;
        }
        return true;
    }
}

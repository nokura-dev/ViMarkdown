using System.Text.RegularExpressions;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private string _searchPattern = "";
    private bool _searchBackward;
    private int _searchOrigin;
    private string _lastSubstitute = "";

    private void FindMatch(bool backward, int count)
    {
        if (_searchPattern.Length == 0) return;
        try
        {
            var matches = Regex.Matches(_pageContent.Text, VimRegex.Pattern(_searchPattern), RegexOptions.Multiline, TimeSpan.FromMilliseconds(250)).Cast<Match>().ToArray();
            if (matches.Length == 0) { _saveStatus.Text = "パターンが見つかりません"; return; }
            while (count-- > 0)
                _caret = backward ? (matches.LastOrDefault(m => m.Index < _caret) ?? matches[^1]).Index : (matches.FirstOrDefault(m => m.Index > _caret) ?? matches[0]).Index;
        }
        catch (ArgumentException) { _saveStatus.Text = "検索パターンが不正です"; }
        catch (RegexMatchTimeoutException) { _saveStatus.Text = "検索がタイムアウトしました"; }
    }
    private void ExecuteEx(string command)
    {
        command = command.Trim();
        if (command is "w" or "write") { SaveCurrent(); return; }
        if (command is "q" or "quit" or "q!") { Close(); return; }
        if (command is "wq" or "x") { if (SaveCurrent()) Close(); return; }
        if (int.TryParse(command, out var number)) { _caret = _source.FirstNonWhitespace(_pageContent.GetLineByNumber(Math.Clamp(number, 1, _pageContent.LineCount)).Offset); return; }
        var match = Regex.Match(command, @"^(?<range>%|\d+(?:,\d+)?|'<,'>)?s(?<body>.*)$");
        if (!match.Success) { _saveStatus.Text = "未対応のコマンド: " + command; return; }
        var body = match.Groups["body"].Value;
        if (body.Length == 0) body = _lastSubstitute;
        if (body.Length == 0) return;
        var delimiter = body[0]; var parts = new List<string>(); var current = "";
        for (var i = 1; i < body.Length; i++)
        {
            if (body[i] == '\\' && i + 1 < body.Length && body[i + 1] == delimiter) { current += delimiter; i++; }
            else if (body[i] == delimiter) { parts.Add(current); current = ""; }
            else current += body[i];
        }
        parts.Add(current); if (parts.Count < 2) { _saveStatus.Text = "置換コマンドが不正です"; return; }
        var flags = parts.Count > 2 ? parts[2] : "";
        if (flags.Any(c => c is not 'g' and not 'i' and not 'I')) { _saveStatus.Text = "未対応の置換フラグ"; return; }
        var range = match.Groups["range"].Value; var first = _source.LineAt(_caret).LineNumber; var last = first;
        if (range == "%") { first = 1; last = _pageContent.LineCount; }
        else if (range.Contains('<') && _lastVisual is { } visual) { first = _source.LineAt(Math.Min(visual.Anchor, visual.Caret)).LineNumber; last = _source.LineAt(Math.Max(visual.Anchor, visual.Caret)).LineNumber; }
        else if (range.Length > 0)
        {
            var numbers = range.Split(',');
            if (!int.TryParse(numbers[0], out first) || !int.TryParse(numbers[^1], out last) || first < 1 || last < first || last > _pageContent.LineCount) { _saveStatus.Text = "行範囲が不正です"; return; }
        }
        try
        {
            var pattern = parts[0].Length > 0 ? parts[0] : _searchPattern;
            var regex = new Regex(VimRegex.Pattern(pattern), flags.Contains('i') ? RegexOptions.IgnoreCase : RegexOptions.None, TimeSpan.FromMilliseconds(250));
            var replacement = parts[1];
            replacement = VimRegex.Replacement(replacement);
            var edits = new List<(int Start, int Length, string Text)>();
            for (var n = first; n <= last; n++)
            {
                var line = _pageContent.GetLineByNumber(n); var text = _pageContent.GetText(line.Offset, line.Length);
                edits.Add((line.Offset, line.Length, regex.Replace(text, replacement, flags.Contains('g') ? int.MaxValue : 1)));
            }
            _editor.Select(edits[0].Start, 0);
            using (_pageContent.RunUpdate())
                foreach (var edit in edits.AsEnumerable().Reverse()) { _editor.Select(edit.Start, 0); _pageContent.Replace(edit.Start, edit.Length, edit.Text); }
            _searchPattern = pattern; _lastSubstitute = body; _caret = _source.FirstNonWhitespace(_pageContent.GetLineByNumber(first).Offset);
        }
        catch (ArgumentException) { _saveStatus.Text = "置換パターンが不正です"; }
        catch (RegexMatchTimeoutException) { _saveStatus.Text = "置換がタイムアウトしました"; }
    }
}

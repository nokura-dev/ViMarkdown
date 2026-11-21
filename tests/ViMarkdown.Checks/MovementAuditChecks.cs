using Avalonia.Input;
using Avalonia.Input.Platform;
using ViMarkdown;

internal static class MovementAuditChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var session = new EditorSession();
        const string text = "one two\nthree four\n\nfive";
        var cases = new[]
        {
            ("h", 1, 0), ("l", 0, 1), ("7l", 0, 7), ("8l", 0, 8),
            ("h", 8, 7), ("2h", 8, 6), ("l", 7, 8),
            ("0", 6, 0), ("^", 6, 0), ("$", 0, 7), ("2$", 0, 18),
            ("j", 0, 8), ("k", 8, 0), ("2j", 0, 19),
            ("w", 0, 4), ("W", 0, 4), ("b", 4, 0), ("B", 4, 0),
            ("e", 0, 2), ("E", 0, 2), ("2w", 0, 8), ("2b", 8, 0),
            ("gg", 14, 0), ("G", 0, 20), ("2gg", 0, 8), ("2G", 0, 8),
            ("{", 14, 0), ("}", 0, 19)
        };
        foreach (var (motion, start, expected) in cases)
        foreach (var visual in new[] { "", "v", "V", "block" })
        {
            session.Reset(text, start == 7 ? 6 : start);
            if (start == 7) session.Keys("l");
            check(session.Rich.Caret == start, "監査の開始位置を確認");
            if (visual == "block") session.Key(PhysicalKey.V, RawInputModifiers.Control);
            else session.Keys(visual);
            session.Keys(motion);
            check(session.Rich.Caret == expected, $"移動監査 {visual}{motion}: {start} -> {session.Rich.Caret}, expected={expected}");
            check(session.Editor.Text == text, "移動・選択は本文を変更しない");
        }
        foreach (var (key, start, expected) in new[]
        {
            (Key.Left, 8, 7), (Key.Right, 6, 7), (Key.Up, 8, 0),
            (Key.Down, 0, 8), (Key.Home, 6, 0), (Key.End, 0, 7)
        })
        {
            session.Reset(text, start);
            session.Window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
            check(session.Rich.Caret == expected, "矢印・Home・Endも対応する移動キーと同じ");
        }
        foreach (var newline in new[] { "\n", "\r\n" })
        {
            var unicode = "A👩‍💻é" + newline + "**日Z**";
            var stops = new[] { 0, 1, 6, 8, 10 + newline.Length, 11 + newline.Length, unicode.Length };
            session.Reset(unicode);
            foreach (var expected in stops.Skip(1))
            {
                session.Keys("l"); check(session.Rich.Caret == expected, "lは書記素と改行を1単位で前進");
            }
            session.Keys("l"); check(session.Rich.Caret == unicode.Length, "文書末尾でlは停止");
            foreach (var expected in stops.Reverse().Skip(1))
            {
                session.Keys("h"); check(session.Rich.Caret == expected, "hは書記素と改行を1単位で後退");
            }
            session.Keys("h"); check(session.Rich.Caret == 0, "文書先頭でhは停止");
        }
        foreach (var keys in new[] { "v8ly", "v$ly" })
        {
            session.Reset(text); session.Keys(keys);
            check(session.Window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult() == "one two\nt", "行を越える文字選択は改行と移動先の文字を含む");
        }
        session.Reset(text, 4); session.Keys("v2ho"); check(session.Rich.Caret == 4, "oで選択の操作端を交換");
        session.Key(PhysicalKey.Escape); session.Keys("gv"); check(session.Rich.Caret == 4, "gvで前回選択を復元");
        session.Reset("abcd\nefgh"); session.Key(PhysicalKey.V, RawInputModifiers.Control);
        session.Keys("jlO"); check(session.Rich.Caret == 5, "矩形選択Oで左右の端を交換");
        session.Keys("o"); check(session.Rich.Caret == 1, "矩形選択oで対角の端を交換");
        session.Reset("one two one"); session.Prompt("/", "one"); check(session.Rich.Caret == 8, "前方検索");
        session.Keys("nN"); check(session.Rich.Caret == 8, "検索の順方向・逆方向反復");
        session.Prompt("?", "two"); check(session.Rich.Caret == 4, "後方検索");
        session.Reset("one two one"); session.Keys("*"); check(session.Rich.Caret == 8, "単語の前方検索");
        session.Keys("#"); check(session.Rich.Caret == 0, "単語の後方検索");
        session.Reset(text); session.Prompt(":", "2"); check(session.Rich.Caret == 8, "Exの行番号移動");
        foreach (var (objectText, keys, expected) in new[]
        {
            ("abc def", "viwy", "abc"), ("abc def", "vawy", "abc "),
            ("abc.def ghi", "viWy", "abc.def"), ("(abc)", "vi(y", "abc"),
            ("[abc]", "va]y", "[abc]"), ("{abc}", "viBy", "abc"),
            ("<abc>", "vi<y", "abc"), ("\"abc\"", "vi\"y", "abc"),
            ("'abc'", "vi'y", "abc"), ("`abc`", "vi`y", "abc"),
            ("abc\ndef\n\nxyz", "vipy", "abc\ndef"),
            ("abc\ndef\n\nxyz", "visy", "abc\ndef")
        })
        {
            session.Reset(objectText, 1); session.Keys(keys);
            check(session.Window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult() == expected, "テキストオブジェクト選択 " + keys);
        }
    }
}

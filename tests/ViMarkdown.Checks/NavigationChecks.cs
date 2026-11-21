using Avalonia.Input;

internal static class NavigationChecks
{
    public static void Run(Action<bool, string> check)
    {
        // Display-only separators can point back to a line start after its glyphs.
        // Reproduce the observed end -> start jump with that projection order.
        var navigationType = typeof(ViMarkdown.MainWindow).Assembly.GetType("ViMarkdown.DisplayNavigation")!;
        var units = Enumerable.Range(0, 6).Select(i => new ViMarkdown.DisplayUnit("abcdef"[i].ToString(), i, i + 1))
            .Append(new ViMarkdown.DisplayUnit("\n", 0, 0)).ToArray();
        var navigation = Activator.CreateInstance(navigationType, new object[] { "abcdef", units });
        var previous = (int)navigationType.GetMethod("PreviousInLine")!.Invoke(navigation, new object[] { 6 })!;
        check(previous == 5, "原文の行頭を指す描画用区切りが末尾にあっても直前の文字へ戻る");
        using var session = new EditorSession();
        var window = session.Window;
        var editor = session.Editor;
        var rich = session.Rich;
        foreach (var (source, first, last) in new[] {
            ("# abc", 2, 4), ("**abc**", 2, 4), ("*abc*", 1, 3),
            ("~~abc~~", 2, 4), ("`abc`", 1, 3), ("[abc](https://example.com)", 1, 3),
            ("> abc", 2, 4), ("term\n: abc", 7, 9), ("```cs\nabc\n```", 6, 8),
            ("abc\n===\n\ntext", 0, 2) })
        {
            session.Reset(source, first);
            session.Key(PhysicalKey.H);
            check(rich.Caret == (source.StartsWith("term") ? 4 : source.StartsWith("> ") ? 0 : first), $"hは非表示記号を飛ばし、前の表示行があれば戻る {source.Replace('\n', ' ')}");
            session.Reset(source, first);
            session.Key(PhysicalKey.L); session.Key(PhysicalKey.L);
            check(rich.Caret == last, $"非表示の終了記号へlで移動しない {source.Replace('\n', ' ')}");
            session.Key(PhysicalKey.Digit0);
            check(rich.Caret == (source.StartsWith("> ") ? 0 : first), "0は引用符を含む表示内容の行頭へ移動");
            session.Key(PhysicalKey.Digit4, RawInputModifiers.Shift);
            check(rich.Caret == editor.Document.GetLineByOffset(last).EndOffset, $"$は原文の行末へ移動 [{source}, actual={rich.Caret}]");
            check(editor.Text == source, "カーソル移動で原文を変更しない");
        }
        session.Reset("> abc", 2);
        session.Key(PhysicalKey.H);
        check(rich.Caret == 0, "引用文のaからhで引用符の前へ移動する");
        session.Key(PhysicalKey.L);
        check(rich.Caret == 2 && editor.Text == "> abc", "引用符の前からlでaへ戻り原文は維持する");
        foreach (var source in new[] { "![abc](asset://x)", "---", "```d2\ngraph TD; A-->B\n```" })
        {
            session.Reset(source, 0);
            session.Key(PhysicalKey.L); session.Key(PhysicalKey.L);
            check(rich.Caret == (source.Contains('\n') ? source.IndexOf('\n') : source.Length), "画像・水平線・D2からlで行末へ移動");
        }
        session.Reset("- [ ] abc", 2);
        session.Key(PhysicalKey.L);
        check(rich.Caret == 5, "チェックボックス内部へ移動しない");
        session.Reset("- abc", 0);
        session.Key(PhysicalKey.L);
        check(rich.Caret == 2, "リストマーカーを一つの表示単位として移動");
        session.Reset("**abc** def", 2);
        session.Key(PhysicalKey.W);
        check(rich.Caret == 8, "wは閉じる装飾記号を飛ばして次の単語へ移動");
        session.Key(PhysicalKey.B);
        check(rich.Caret == 2, "bは開く装飾記号を飛ばして表示単語へ移動");
        session.Key(PhysicalKey.E);
        check(rich.Caret == 4, "eは表示単語の末尾へ移動");
        session.Reset("[abc](url) def", 3);
        session.Key(PhysicalKey.E);
        check(rich.Caret == 13, "eはリンクURLを移動対象に含めない");
        session.Reset("```cs\nabc\n```\n\nend", 6);
        session.Key(PhysicalKey.K);
        check(rich.Caret == 6, "kは先頭のコードフェンスへ移動しない");
        session.Key(PhysicalKey.J);
        check(rich.Caret > 12, "jは閉じるコードフェンスを飛ばす");
        session.Reset("abc\n===\n\ntext", 0);
        session.Key(PhysicalKey.J);
        check(rich.Caret >= 8, "jは見出しの下線を移動対象に含めない");
        session.Reset("a **bc** d", 0);
        session.Key(PhysicalKey.Digit3); session.Key(PhysicalKey.L);
        check(rich.Caret == 5, "回数指定のlは表示文字を数える");
        session.Reset("abcdef\nx\nabcdef", 4);
        session.Key(PhysicalKey.J); session.Key(PhysicalKey.J);
        check(rich.Caret == 13, "短い行を経由しても上下移動の表示列を維持");
        session.Reset("**abc**\ndef", 2);
        session.Key(PhysicalKey.J);
        check(rich.Caret == 8, "jは終了記号のある行を越えて次の表示行へ移動");
        session.Reset("abc\n", 0);
        session.Key(PhysicalKey.J);
        check(rich.Caret == 4, "末尾の空行へ移動できる");
        session.Reset("**👩‍💻é日**", 2);
        session.Key(PhysicalKey.L);
        check(rich.Caret == 7, "表示移動で複合絵文字を分割しない");
        session.Key(PhysicalKey.L);
        check(rich.Caret == 9, "表示移動で結合文字を分割しない");
    }
}

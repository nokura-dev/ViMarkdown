internal static class ParagraphNavigationChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var session = new EditorSession();
        foreach (var newline in new[] { "\n", "\r\n" })
        foreach (var trailing in new[] { "", newline, newline + newline })
        {
            var text = "aaa" + newline + newline + "bbb" + trailing;
            session.Reset(text); session.Keys("10}");
            var end = session.Rich.Caret;
            session.Keys("{");
            check(session.Rich.Caret < end, "文末まで}で進んだ後も{で戻れる");
            session.Keys("10{");
            check(session.Rich.Caret == 0, "{を繰り返して文頭へ戻れる");
            session.Keys("}");
            check(session.Rich.Caret == 3 + newline.Length, "文頭から}で次の段落境界へ進める");
            check(session.Editor.Text == text, "段落移動で本文を変更しない");
        }
        foreach (var text in new[] { "", "\n", "\n\n", "abc" })
        {
            session.Reset(text); session.Keys("10}10{");
            check(session.Rich.Caret == 0, "空文書・空行のみ・1段落でも境界で停止し復帰できる");
        }
    }
}

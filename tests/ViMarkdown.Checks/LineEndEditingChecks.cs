using Avalonia.Headless;
using Avalonia.Input;

internal static class LineEndEditingChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var session = new EditorSession();
        foreach (var (text, start, keys, expected) in new[]
        {
            ("abcd", 2, "d$", "ab"),
            ("abc\r\ndef", 1, "d$", "a\r\ndef"),
            ("前👩‍💻後", 6, "d$", "前👩‍💻"),
            ("abc\ndef\nghi", 1, "d2$", "a\nghi"),
            ("abc", 0, "d$", "")
        })
        {
            session.Reset(text, start); session.Keys(keys);
            check(session.Editor.Text == expected && session.Rich.Caret == start,
                keys + "は行末まで削除し、削除開始位置（残った文字の後ろ）を維持");
        }
        session.Reset("abcd", 2); session.Keys("d$"); session.Keys("h");
        check(session.Rich.Caret == 1, "d$の直後のhは1文字だけ戻る");
        session.Reset("abcd", 2); session.Keys("c$"); session.Window.KeyTextInput("X");
        check(session.Editor.Text == "abX", "c$は削除位置から挿入");
        session.Key(PhysicalKey.Escape);
        session.Reset("abcd", 2); session.Keys("d0");
        check(session.Editor.Text == "cd" && session.Rich.Caret == 0, "d0の削除範囲と位置を維持");
        session.Reset("abcd", 2); session.Keys("c0"); session.Window.KeyTextInput("X");
        check(session.Editor.Text == "Xcd", "c0の変更範囲と挿入位置を維持");
    }
}

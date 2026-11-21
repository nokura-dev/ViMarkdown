using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using ViMarkdown;

internal static class HistoryChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var session = new EditorSession();
        var window = session.Window;
        var editor = session.Editor;
        var rich = session.Rich;
        void Reset()
        {
            // Set up an insertion in the middle, with enough following text
            // that clamping to document length cannot hide a stale offset.
            session.Click("INSERT (i)");
            editor.Document.Text = "0123456789abcdefghij";
            editor.Select(4, 0);
            editor.Document.UndoStack.ClearAll();
            Dispatcher.UIThread.RunJobs();
        }
        const string original = "0123456789abcdefghij";
        foreach (var text in new[] { "日本語", "ABC", "👩‍💻é", "\n追加\n" })
        {
            Reset();
            window.KeyTextInput(text);
            session.Key(PhysicalKey.Escape);
            var insertionEnd = 4 + text.Length;
            var normalPosition = editor.Document.GetLineByOffset(insertionEnd).Offset == insertionEnd
                ? insertionEnd : TextBoundaries.Previous(editor.Text, insertionEnd);
            check(rich.Caret == normalPosition, "Escで入力した最後の書記素へ移動");
            session.Key(PhysicalKey.End); // Undo must return to the edit, even after movement.
            session.Key(PhysicalKey.U);
            check(editor.Document.Text == original && rich.Caret == 4,
                $"uで編集箇所へ復帰: {text.Replace('\n', ' ')}");
            session.Key(PhysicalKey.I);
            check(editor.CaretOffset == 4, "Undo後の挿入位置も編集箇所と一致");
            session.Key(PhysicalKey.Escape);
            session.Key(PhysicalKey.Home);
            session.Key(PhysicalKey.R, RawInputModifiers.Control);
            check(editor.Document.Text == original.Insert(4, text) && rich.Caret == 4 + text.Length,
                "Ctrl+Rで本文と編集後のカーソルを復元");
            for (var i = 0; i < 2; i++)
            {
                session.Key(PhysicalKey.U);
                check(rich.Caret == 4, "Undo反復で位置が累積してずれない");
                session.Key(PhysicalKey.R, RawInputModifiers.Control);
                check(rich.Caret == 4 + text.Length, "Redo反復で位置が累積してずれない");
            }
        }
        Reset();
        session.Key(PhysicalKey.Escape);
        session.Key(PhysicalKey.L);
        session.Key(PhysicalKey.L);
        session.Key(PhysicalKey.L);
        session.Key(PhysicalKey.U);
        check(rich.Caret == 6 && editor.Document.Text == original, "履歴がないuでカーソルが動かない");
        session.Key(PhysicalKey.R, RawInputModifiers.Control);
        check(rich.Caret == 6, "履歴がないCtrl+Rでカーソルが動かない");
        session.Key(PhysicalKey.X);
        check(editor.Document.Text == original.Remove(6, 1), "移動後のxで対象文字を削除");
        session.Key(PhysicalKey.U);
        check(editor.Document.Text == original && rich.Caret == 6, "xのUndoで削除箇所へ復帰");
        session.Key(PhysicalKey.R, RawInputModifiers.Control);
        check(editor.Document.Text == original.Remove(6, 1) && rich.Caret == 6, "xのRedoで削除箇所を維持");
        session.Key(PhysicalKey.U);
        session.Key(PhysicalKey.V);
        session.Key(PhysicalKey.L);
        session.Key(PhysicalKey.L);
        session.Key(PhysicalKey.X);
        session.Key(PhysicalKey.U);
        check(editor.Document.Text == original && rich.Caret == 6 && rich.Anchor is null,
            "ビジュアル削除のUndoで削除範囲の先頭へ復帰");
    }
}

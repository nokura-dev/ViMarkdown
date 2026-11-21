using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

internal static class ImeChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var session = new EditorSession();
        var window = session.Window;
        var editor = session.Editor;
        session.Click("INSERT (i)");
        editor.Text = "ABC本文";
        editor.CaretOffset = 0;
        editor.Document.UndoStack.ClearAll();
        var request = new TextInputMethodClientRequestedEventArgs
        {
            RoutedEvent = InputElement.TextInputMethodClientRequestedEvent
        };
        editor.TextArea.RaiseEvent(request);
        var client = request.Client!;
        check(client is { SupportsPreedit: true }, "挿入本文はIMEに未確定文字の表示対応を通知する");
        var layer = editor.TextArea.TextView.GetVisualDescendants().OfType<Control>()
            .Single(c => c.GetType().Name == "CompositionLayer");
        byte[] Draw()
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(600, 200), new Vector(96, 96));
            bitmap.Render(layer);
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            return stream.ToArray();
        }
        var empty = Draw();
        var original = editor.Text;
        foreach (var text in new[] { "に", "にほ", "にほん", "日本" })
        {
            client.SetPreeditText(text, text.Length);
            check(!Draw().SequenceEqual(empty), $"IME未確定文字「{text}」を変換確定前に描画する");
            check(editor.Text == original && !editor.Document.UndoStack.CanUndo,
                "未確定文字は本文・Undo履歴へ書き込まない");
        }
        client.SetPreeditText("にほん", 0);
        var first = client.CursorRectangle;
        client.SetPreeditText("にほん", 3);
        check(client.CursorRectangle.X > first.X, "IME候補の表示位置は未確定文字内のカーソルに追従する");
        window.KeyTextInput("日本");
        Dispatcher.UIThread.RunJobs();
        check(editor.Text == "日本" + original && Draw().SequenceEqual(empty),
            "IME確定時は本文へ一度だけ挿入して未確定表示を消す");
        editor.Document.UndoStack.Undo();
        check(editor.Text == original, "IME確定入力を一度のUndoで取り消せる");
        client.SetPreeditText("取消", null);
        session.Key(PhysicalKey.Escape);
        check(editor.IsVisible && editor.Text == original && Draw().SequenceEqual(empty),
            "未確定中のEscは入力を取り消し挿入モードを維持する");
        client.SetPreeditText("編集中");
        session.Key(PhysicalKey.ArrowLeft, RawInputModifiers.Control | RawInputModifiers.Shift);
        check(Draw().SequenceEqual(empty) && editor.Text == original,
            "フォーカスが外れたら未確定表示を消し本文は保持する");
        editor.TextArea.Focus();
        client.SetPreeditText("旧ページ");
        editor.Document.Text = "別の本文";
        check(Draw().SequenceEqual(empty), "本文の置換後に旧未確定文字を残さない");
    }
}

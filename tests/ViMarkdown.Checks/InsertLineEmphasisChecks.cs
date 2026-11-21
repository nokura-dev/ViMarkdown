using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaEdit.Editing;

internal static class InsertLineEmphasisChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var session = new EditorSession();
        var window = session.Window;
        session.Click("INSERT (i)");
        var editor = session.Editor;
        var area = editor.TextArea;
        const string source = "first\n\n";
        editor.Text = source + string.Concat(Enumerable.Repeat("wrapped text ", 60)) + "\nlast";
        editor.Document.Insert(editor.Document.TextLength, "!");
        var original = editor.Text;
        editor.CaretOffset = 0;
        Dispatcher.UIThread.RunJobs();
        var margin = area.LeftMargins.OfType<LineNumberMargin>().Single();
        byte[] RenderNumbers()
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(margin.Bounds.Width), (int)Math.Ceiling(margin.Bounds.Height)));
            bitmap.Render(margin);
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            return stream.ToArray();
        }
        var firstNumbers = RenderNumbers();
        var width = margin.Bounds.Width;
        var renderer = area.TextView.BackgroundRenderers.Single(r => r.GetType().Name == "InsertLineUnderline");
        Rect Underline()
        {
            var drawing = new DrawingGroup();
            using (var context = drawing.Open()) renderer.Draw(area.TextView, context);
            return drawing.Children.OfType<GeometryDrawing>().Single().Geometry!.Bounds.Inflate(.5);
        }
        var first = Underline();
        check(Math.Abs(first.Width - area.TextView.Bounds.Width) < 2 && first.Height <= 2, "カーソル行の下線は本文領域の全幅に1pxで描画");
        editor.CaretOffset = 6; // Empty second line.
        Dispatcher.UIThread.RunJobs();
        var empty = Underline();
        check(empty.Top > first.Top && !firstNumbers.SequenceEqual(RenderNumbers()), "空行へのカーソル移動で下線と行番号の描画が更新");
        check(Math.Abs(margin.Bounds.Width - width) < .1, "行番号の太字切替で本文の開始位置が動かない");
        editor.CaretOffset = source.Length + 200;
        editor.TextArea.Caret.BringCaretToView();
        Dispatcher.UIThread.RunJobs();
        check(Underline().Top > empty.Top, "折り返し行でもカーソルのある表示行に下線が追従");
        check(editor.Text == original && editor.Document.UndoStack.CanUndo, "行の強調は原文とUndo履歴を保持");
    }
}

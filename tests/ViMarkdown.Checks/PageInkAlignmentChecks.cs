using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViMarkdown;

internal static class PageInkAlignmentChecks
{
    public static void Run(Action<bool, string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "markdown-ink-" + Guid.NewGuid() + ".db");
        try
        {
            using (var store = new WorkspaceStore(path))
            {
                var firstLines = new[] { "本文 English", "English", "lowercase", "ÁÉ accents", "# Heading", "# 見出し",
                    "## 見出し", "### Heading", "*italic*", "**Bold**", "`code`", "👩‍💻 emoji", "é combining",
                    "English " + string.Concat(Enumerable.Repeat("wrapped text ", 80)) + "👩‍💻" };
                foreach (var text in firstLines) store.Create(text[..Math.Min(20, text.Length)], false, null, text + "\n\n本文");
                store.SetSetting("initialized", "1");
            }
            using var session = new EditorSession(path);
            var window = session.Window;
            var editor = session.Editor;
            var rich = session.Rich;
            var pages = window.GetVisualDescendants().OfType<ListBox>()
                .Single(list => list.ItemsSource?.Cast<object>().Any(item => item is WorkspaceItem) == true);
            foreach (var width in new[] { 1400, 1100 })
            {
                window.Width = width;
                Dispatcher.UIThread.RunJobs();
                var expected = new Dictionary<double, Point>();
                foreach (var page in pages.ItemsSource!.Cast<WorkspaceItem>().ToArray())
                {
                    pages.SelectedItem = page;
                    Dispatcher.UIThread.RunJobs();
                    foreach (var mode in new[] { "NORMAL (Esc)", "INSERT (i)", "VISUAL (v)", "NORMAL (Esc)" })
                    {
                        session.Click(mode);
                        using var layout = new RichTextLayout(Projection.Build(editor.Text),
                            rich.Bounds.Width - rich.ContentPadding.Left - rich.ContentPadding.Right,
                            lineHeight: Projection.Build(editor.Text).All(unit => unit.Size <= Typography.Body) ? rich.BodyLineHeight : double.NaN);
                        var line = editor.IsVisible ? editor.TextArea.TextView.VisualLines[0].TextLines[0] : layout.Layout.TextLines[0];
                        var origin = editor.IsVisible ? editor.TextArea.TextView.TranslatePoint(default, window)!.Value
                            : rich.TranslatePoint(rich.ContentOrigin, window)!.Value;
                        foreach (var scale in new[] { 1.0, 1.25, 2.0 })
                        {
                            var actual = InkBounds(line, origin, scale);
                            if (!expected.TryGetValue(scale, out var anchor)) expected[scale] = anchor = actual;
                            // Native color emoji use bitmap strikes whose ink
                            // edges vary with scale/subpixel phase. Bundled text
                            // retains the stricter one-physical-pixel contract.
                            var tolerance = page.Name.StartsWith("👩‍💻", StringComparison.Ordinal) ? 2 : 1;
                            check(Math.Abs(actual.X - anchor.X) <= tolerance && Math.Abs(actual.Y - anchor.Y) <= tolerance,
                                $"描画された文字の縁がページ・モード間で{tolerance}物理px以内に揃う ({width}px・{scale}倍・{mode}・{page.Name}) [{anchor}/{actual}]");
                        }
                    }
                }
            }
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
        }
        CheckEditingAndIndent(check);
    }

    private static void CheckEditingAndIndent(Action<bool, string> check)
    {
        using var session = new EditorSession();
        session.Click("INSERT (i)");
        var editor = session.Editor;
        editor.Text = "English\n本文";
        editor.CaretOffset = 0;
        editor.Document.UndoStack.ClearAll();
        Dispatcher.UIThread.RunJobs();
        Point Start()
        {
            var view = editor.TextArea.TextView;
            return InkBounds(view.VisualLines[0].TextLines[0], view.TranslatePoint(default, session.Window)!.Value);
        }
        var before = Start();
        session.Window.KeyTextInput("Á");
        Dispatcher.UIThread.RunJobs();
        var after = Start();
        check(editor.Text == "ÁEnglish\n本文" && Math.Abs(after.X - before.X) <= 1 && Math.Abs(after.Y - before.Y) <= 1,
            "先頭へアクセント付き文字を入力しても文字の開始位置を維持");
        session.Click("Undo");
        check(editor.Text == "English\n本文" && Start() == before, "Undo後も文字の開始位置を復元");
        editor.CaretOffset = 0;
        session.Window.KeyTextInput("  ");
        Dispatcher.UIThread.RunJobs();
        using var indent = new Avalonia.Media.TextFormatting.TextLayout("  ",
            new Typeface(editor.FontFamily), editor.FontSize, Brushes.Black);
        check(Math.Abs(Start().X - before.X - indent.WidthIncludingTrailingWhitespace) <= 1 && editor.Text.StartsWith("  English"),
            "位置補正は原文に入力された字下げを打ち消さない");
        var original = editor.Text;
        session.Click("NORMAL (Esc)");
        session.Click("VISUAL (v)");
        session.Click("INSERT (i)");
        check(editor.Text == original && editor.Document.UndoStack.CanUndo,
            "位置補正とモード往復で原文・Undo履歴を保持");
    }

    // Rasterize the actual shaped line at the same subpixel phase as the window.
    // Read visible text pixels, excluding pale syntax backgrounds and the caret.
    internal static Point InkBounds(TextLine line, Point origin, double scale = 1)
    {
        var size = new PixelSize((int)(600 * scale), (int)(100 * scale));
        using var target = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        using (var context = target.CreateDrawingContext())
        {
            context.FillRectangle(Brushes.White, new Rect(0, 0, 600, 100));
            line.Draw(context, new Point(20 + origin.X - Math.Floor(origin.X * scale) / scale,
                20 + origin.Y - Math.Floor(origin.Y * scale) / scale));
        }
        using var stream = new MemoryStream();
        target.Save(stream); stream.Position = 0;
        using var bitmap = new Bitmap(stream);
        var stride = size.Width * 4;
        var bytes = new byte[stride * size.Height];
        var address = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), address, bytes.Length, stride);
            Marshal.Copy(address, bytes, 0, bytes.Length);
        }
        finally { Marshal.FreeHGlobal(address); }
        var minX = size.Width; var minY = size.Height;
        for (var y = 0; y < size.Height; y++)
            for (var x = 0; x < size.Width; x++)
            {
                var at = y * stride + x * 4;
                if (Math.Min(bytes[at], Math.Min(bytes[at + 1], bytes[at + 2])) < 180)
                {
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                }
            }
        if (minX == size.Width) throw new InvalidOperationException("No visible text pixels in the first line.");
        return new Point(minX - 20 * scale + Math.Floor(origin.X * scale),
            minY - 20 * scale + Math.Floor(origin.Y * scale));
    }
}

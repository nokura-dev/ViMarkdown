using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViMarkdown;

internal static class ModeAlignmentChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var session = new EditorSession();
        var window = session.Window;
        var editor = session.Editor;
        var rich = session.Rich;
        editor.Text = "2026/11/19 21:34\nあいうえお\n\nコレクション\n今度のコンペはメインで任せてもらえそうで\n出たお節介男";
        session.Click("INSERT (i)");
        Dispatcher.UIThread.RunJobs();
        check(Math.Abs(rich.BodyLineHeight - editor.TextArea.TextView.DefaultLineHeight) < .01,
            "ノーマル本文には挿入モードで実測した行高を使用する");
        using (var bodyLayout = new RichTextLayout(Projection.Build(editor.Text), 900, lineHeight: rich.BodyLineHeight))
        {
            var sourceLines = editor.TextArea.TextView.VisualLines.SelectMany(line => line.TextLines).ToArray();
            var normalLines = bodyLayout.Layout.TextLines;
            double sourceTop = 0, normalTop = 0;
            for (var i = 0; i < sourceLines.Length; i++)
            {
                check(Math.Abs(sourceTop - normalTop) < .01,
                    $"日本語・英数字・空行が混在しても{i + 1}行目の縦位置が一致 [{sourceTop}/{normalTop}]");
                check(Math.Abs(sourceLines[i].Height - normalLines[i].Height) < .01,
                    $"本文の{i + 1}行目の行高が一致 [{sourceLines[i].Height}/{normalLines[i].Height}]");
                sourceTop += sourceLines[i].Height;
                normalTop += normalLines[i].Height;
            }
        }
        // Page/count/font alignment is covered by CheckPageAlignment and PageInkAlignmentChecks.
        // Headings and wrapping make source and rich document heights differ.
        session.Click("INSERT (i)");
        editor.Document.Text = string.Join("\n", Enumerable.Range(0, 100)
            .Select(i => i % 3 == 0 ? "# 見出し " + i : "本文 **強調** " + new string('文', 100)));
        Dispatcher.UIThread.RunJobs();
        editor.CaretOffset = editor.Document.GetLineByNumber(20).Offset + 5;
        var sourceScroll = editor.TextArea.TextView.GetVisualAncestors().OfType<ScrollViewer>().First();
        sourceScroll.Offset = new Vector(0, editor.TextArea.TextView.GetVisualPosition(editor.TextArea.Caret.Position,
            AvaloniaEdit.Rendering.VisualYPosition.LineMiddle).Y - 200);
        Dispatcher.UIThread.RunJobs();
        double SourceY()
        {
            var view = editor.TextArea.TextView;
            return view.TranslatePoint(view.GetVisualPosition(editor.TextArea.Caret.Position,
                AvaloniaEdit.Rendering.VisualYPosition.LineMiddle) - view.ScrollOffset, window)!.Value.Y;
        }
        var expectedY = SourceY();
        check(expectedY > 100 && expectedY < window.Bounds.Height - 100,
            $"切替テストのカーソルは画面内に表示 [{expectedY}]");
        var expectedOffset = editor.CaretOffset;
        foreach (var mode in new[] { "NORMAL (Esc)", "VISUAL (v)", "INSERT (i)", "VISUAL (v)", "NORMAL (Esc)", "INSERT (i)" })
        {
            session.Click(mode);
            var actualY = editor.IsVisible ? SourceY() : rich.TranslatePoint(rich.CaretBounds.Center, window)!.Value.Y;
            check(Math.Abs(actualY - expectedY) < 2,
                $"{mode}への切替でカーソルの画面上の高さを維持 [{expectedY}/{actualY}]");
            check((editor.IsVisible ? editor.CaretOffset : rich.Caret) == expectedOffset,
                $"{mode}への切替で原文のカーソル位置を維持");
        }
        session.Click("NORMAL (Esc)");
        // Move only in Normal: the hidden source editor must start elsewhere.
        for (var step = 0; step < 45; step++)
        {
            session.Key(PhysicalKey.J);
        }
        var normalY = rich.TranslatePoint(rich.CaretBounds.Center, window)!.Value.Y;
        var normalCaret = rich.Caret;
        var oldSourceOffset = editor.TextArea.TextView.ScrollOffset.Y;
        session.Click("INSERT (i)");
        check(Math.Abs(SourceY() - normalY) < 2,
            $"ノーマルで移動後の挿入切替でカーソルの高さを維持 [{normalY}/{SourceY()}]");
        check(editor.CaretOffset == normalCaret, "ノーマルで移動した原文位置を挿入へ引き継ぐ");
        check(Math.Abs(editor.TextArea.TextView.ScrollOffset.Y - oldSourceOffset) > 100,
            "ノーマルから挿入への切替で原文エディタが実際にスクロールする");
        CheckFirstInsert(check);
        CheckPageAlignment(check);
    }

    private static void CheckPageAlignment(Action<bool, string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "markdown-alignment-" + Guid.NewGuid() + ".db");
        using (var store = new WorkspaceStore(path))
        {
            foreach (var count in new[] { 1, 9, 10, 99, 100, 999, 1000 })
                store.Create($"本文{count}", false, null,
                    "本文 English 日本語" + string.Concat(Enumerable.Repeat("\n本文", count - 1)));
            store.Create("見出し", false, null, "# 見出し English 日本語\n\n本文");
            store.Create("空ページ", false, null, "");
            store.SetSetting("initialized", "1");
        }
        try
        {
            using var session = new EditorSession(path, height: 480);
            var window = session.Window;
            var editor = session.Editor;
            var rich = session.Rich;
            var pages = window.GetVisualDescendants().OfType<ListBox>()
                .Single(list => list.ItemsSource?.Cast<object>().Any(item => item is WorkspaceItem) == true);
            Point? expected = null;
            foreach (var mode in new[] { "NORMAL (Esc)", "INSERT (i)", "VISUAL (v)" })
            {
                session.Click(mode);
                var items = pages.ItemsSource!.Cast<WorkspaceItem>().ToArray();
                // Visit long and short pages in both directions, with the previous
                // page scrolled to its end before opening the next one.
                foreach (var page in items.Concat(items.Reverse()))
                {
                    if ((pages.SelectedItem as WorkspaceItem)?.Id == page.Id) continue;
                    pages.SelectedItem = page;
                    Dispatcher.UIThread.RunJobs();
                    window.CaptureRenderedFrame()?.Dispose();
                    Dispatcher.UIThread.RunJobs();
                    Point start;
                    if (editor.IsVisible)
                    {
                        var view = editor.TextArea.TextView;
                        var line = view.VisualLines[0].TextLines[0];
                        var sourceOrigin = view.TranslatePoint(default(Point) - view.ScrollOffset, window)!.Value;
                        start = page.Body.Length > 0 ? PageInkAlignmentChecks.InkBounds(line, sourceOrigin) : sourceOrigin;
                        check(editor.CaretOffset == 0 && view.ScrollOffset.Y < .1,
                            $"ページ切替で挿入カーソルとスクロールが先頭に戻る ({page.Name}) [{editor.CaretOffset}/{view.ScrollOffset.Y}]");
                    }
                    else
                    {
                        using var layout = new RichTextLayout(Projection.Build(editor.Text),
                            rich.Bounds.Width - rich.ContentPadding.Left - rich.ContentPadding.Right,
                            lineHeight: Projection.Build(editor.Text).All(unit => unit.Size <= Typography.Body) ? rich.BodyLineHeight : double.NaN);
                        var richOrigin = rich.TranslatePoint(rich.ContentOrigin, window)!.Value;
                        start = page.Body.Length > 0 ? PageInkAlignmentChecks.InkBounds(layout.Layout.TextLines[0], richOrigin) : richOrigin;
                        check(rich.GetVisualAncestors().OfType<ScrollViewer>().First().Offset.Y < .1,
                            $"ページ切替でリッチ表示のスクロールが先頭に戻る ({page.Name})");
                    }
                    // Empty lines have no glyph ink; compare their horizontal
                    // origin only. The next nonempty page still uses the same top.
                    if (page.Body.Length > 0) expected ??= start;
                    if (expected is { } origin)
                    {
                        check(Math.Abs(start.X - origin.X) <= 1,
                            $"ページ・モード間で行頭が一致 ({mode}・{page.Name}) [{origin.X}/{start.X}]");
                        if (page.Body.Length > 0)
                            check(Math.Abs(start.Y - origin.Y) <= 1,
                                $"ページ・モード間で文字上端が一致 ({mode}・{page.Name}) [{origin.Y}/{start.Y}]");
                    }
                    if (editor.IsVisible)
                    {
                        editor.CaretOffset = editor.Document.TextLength;
                        editor.TextArea.Caret.BringCaretToView();
                        Dispatcher.UIThread.RunJobs();
                    }
                    else session.Keys("G");
                }
            }
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
        }

    }

    private static void CheckFirstInsert(Action<bool, string> check)
    {
        using var session = new EditorSession();
        var window = session.Window;
        var editor = session.Editor;
        var rich = session.Rich;
        editor.Document.Text = string.Join("\n", Enumerable.Range(0, 150)
            .Select(i => i % 4 == 0 ? "# 見出し " + i : new string('文', 100)));
        Dispatcher.UIThread.RunJobs();
        for (var step = 0; step < 60; step++)
        {
            session.Key(PhysicalKey.J);
        }
        var expectedY = rich.TranslatePoint(rich.CaretBounds.Center, window)!.Value.Y;
        var expectedCaret = rich.Caret;
        session.Click("INSERT (i)");
        window.CaptureRenderedFrame()?.Dispose();
        Dispatcher.UIThread.RunJobs();
        var view = editor.TextArea.TextView;
        var actualY = view.TranslatePoint(view.GetVisualPosition(editor.TextArea.Caret.Position,
            AvaloniaEdit.Rendering.VisualYPosition.LineMiddle) - view.ScrollOffset, window)!.Value.Y;
        check(editor.CaretOffset == expectedCaret && view.ScrollOffset.Y > 0 && Math.Abs(actualY - expectedY) < 2,
            $"初回のノーマル→挿入でも描画後までカーソルの高さを維持 [{expectedY}/{actualY}]");
    }
}

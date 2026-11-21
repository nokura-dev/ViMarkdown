using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViMarkdown;

internal static class ScrollChecks
{
    internal static void CheckThumbAtTop(ScrollViewer scroll, Action<bool, string> check, string label)
    {
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>()
            .First(b => b.Orientation == Orientation.Vertical);
        var track = bar.GetVisualDescendants().OfType<Track>().Single();
        var thumb = track.Thumb!;
        var thumbTop = thumb.TranslatePoint(default, track)!.Value.Y;
        var trackTop = track.TranslatePoint(default, bar)!.Value.Y;
        check(Math.Abs(bar.Value - bar.Minimum) < .1 && Math.Abs(track.Value - track.Minimum) < .1 && Math.Abs(thumbTop) < 1 && trackTop < 2,
            $"{label}: 先頭でつまみと値が上端に到達 (offset={scroll.Offset.Y}, bar={bar.Value}, min={bar.Minimum}, track={track.Value}, thumbY={thumbTop}, trackY={trackTop})");
    }

    internal static void CheckThumbAtBottom(ScrollViewer scroll, Action<bool, string> check, string label)
    {
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>()
            .First(b => b.Orientation == Orientation.Vertical);
        var track = bar.GetVisualDescendants().OfType<Track>().Single();
        var thumb = track.Thumb!;
        var bottom = thumb.TranslatePoint(new Point(0, thumb.Bounds.Height), bar)!.Value.Y;
        var trackBottom = track.TranslatePoint(new Point(0, track.Bounds.Height), bar)!.Value.Y;
        check(Math.Abs(bar.Value - bar.Maximum) < 1 && Math.Abs(bottom - bar.Bounds.Height) <= 1.01 &&
            Math.Abs(trackBottom - bar.Bounds.Height) <= 1.01,
            $"{label}: 末尾でつまみと値が下端に到達 (offset={scroll.Offset.Y}, bar={bar.Value}, max={bar.Maximum}, thumbBottom={bottom}, height={bar.Bounds.Height})");
    }

    internal static void CheckActivity(Window window, ScrollViewer scroll, Action<bool, string> check,
        string label, bool drag = false)
    {
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>()
            .First(b => b.Orientation == Orientation.Vertical);
        void PumpUntil(Func<bool> predicate)
        {
            if (predicate()) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            poll.Tick += (_, _) => { if (predicate()) timeout.Cancel(); };
            poll.Start();
            try { Dispatcher.UIThread.MainLoop(timeout.Token); }
            finally { poll.Stop(); }
        }
        PumpUntil(() => bar.Opacity == 0);
        var viewport = scroll.Viewport;
        check(bar.Opacity == 0 && !bar.IsHitTestVisible, $"{label}: 停止中はスクロールバーを隠す (opacity={bar.Opacity}, hit={bar.IsHitTestVisible})");
        var barPoint = bar.TranslatePoint(new Point(bar.Bounds.Width / 2, 20), window)!.Value;
        window.MouseMove(barPoint);
        Dispatcher.UIThread.RunJobs();
        check(bar.Opacity == 0, label + ": マウスを重ねるだけでは表示しない");
        var point = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), window)!.Value;
        window.MouseWheel(point, new Vector(0, -1));
        Dispatcher.UIThread.RunJobs();
        check(bar.Opacity == 1 && bar.IsHitTestVisible, label + ": ホイールでスクロール中は表示する");
        if (drag)
        {
            var thumb = bar.GetVisualDescendants().OfType<Track>().Single().Thumb!;
            var grip = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
            window.MouseMove(grip);
            window.MouseDown(grip, MouseButton.Left);
            using (var hold = new CancellationTokenSource(TimeSpan.FromMilliseconds(800)))
                Dispatcher.UIThread.MainLoop(hold.Token);
            check(bar.Opacity == 1, label + ": つまみを保持中は消えない");
            window.MouseMove(grip + new Vector(0, 20), RawInputModifiers.LeftMouseButton);
            window.MouseUp(grip + new Vector(0, 20), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
        PumpUntil(() => bar.Opacity == 0);
        check(bar.Opacity == 0 && !bar.IsHitTestVisible && scroll.Viewport == viewport,
            label + ": 操作終了後に隠し、表示切替で本文の幅を変えない");
        scroll.ScrollToHome();
        Dispatcher.UIThread.RunJobs();
    }

    public static void Run(Action<bool, string> check)
    {
        using var session = new EditorSession(height: 480);
        var window = session.Window;
        var editor = session.Editor;
        var rich = session.Rich;
        var scroll = rich.GetVisualAncestors().OfType<ScrollViewer>().First();
        session.Click("INSERT (i)");
        foreach (var count in new[] { 30, 80 })
        {
            editor.Document.Text = string.Join("\n", Enumerable.Range(1, count).Select(i => $"行{i}"));
            Dispatcher.UIThread.RunJobs();
            editor.CaretOffset = editor.Text.Length;
            editor.TextArea.Caret.BringCaretToView();
            Dispatcher.UIThread.RunJobs();
            var sourceScroll = editor.GetVisualDescendants().OfType<ScrollViewer>().First();
            check(sourceScroll.Extent.Height > sourceScroll.Viewport.Height &&
                Math.Abs(sourceScroll.Extent.Height - sourceScroll.Viewport.Height - sourceScroll.Offset.Y) < 2,
                "挿入モードの文書末尾でスクロールバーが下端に到達");
            CheckThumbAtBottom(sourceScroll, check, "挿入モード");
            editor.CaretOffset = 0;
            editor.TextArea.Caret.BringCaretToView();
            Dispatcher.UIThread.RunJobs();
            CheckThumbAtTop(sourceScroll, check, "挿入モード");
            check(sourceScroll.Offset.Y < 1, "挿入モードの先頭でスクロールバーが上端に戻る");
            if (count == 30) CheckActivity(window, sourceScroll, check, "挿入モード", drag: true);
        }
        foreach (var lineCount in new[] { 30, 80 })
            foreach (var visual in new[] { false, true })
            {
                session.Click("INSERT (i)");
                editor.Document.Text = string.Join("\n", Enumerable.Range(1, lineCount).Select(i => $"行{i}"));
                editor.Select(0, 0);
                session.Key(PhysicalKey.Escape);
                scroll.Offset = default;
                if (visual) session.Key(PhysicalKey.V);
                for (var i = 0; i < 50; i++) session.Key(PhysicalKey.J);
                check(scroll.Offset.Y > 0, $"{(visual ? "ビジュアル" : "ノーマル")}のjで下端を越えるとスクロール");
                {
                    var caret = rich.CaretBounds;
                    check(caret.Top >= scroll.Offset.Y && caret.Bottom <= scroll.Offset.Y + scroll.Viewport.Height,
                        "スクロール後もカーソル全体が表示領域内にある");
                }
                for (var i = 0; i < 150; i++) session.Key(PhysicalKey.J);
                check(Math.Abs(scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y) < 1,
                    "最終行ではスクロールバーが下端に到達");
                CheckThumbAtBottom(scroll, check, visual ? "選択モード" : "ノーマルモード");
                var downOffset = scroll.Offset.Y;
                for (var i = 0; i < 200; i++) session.Key(PhysicalKey.K);
                CheckThumbAtTop(scroll, check, visual ? "選択モード" : "ノーマルモード");
                if (lineCount == 30) CheckActivity(window, scroll, check, visual ? "選択モード" : "ノーマルモード");
                check(rich.Caret <= editor.Text.IndexOf('\n') && scroll.Offset.Y < downOffset && scroll.Offset.Y < 1,
                    "kで先頭へ戻ると上方向にもスクロールしてカーソルを表示");
            }
    }
}

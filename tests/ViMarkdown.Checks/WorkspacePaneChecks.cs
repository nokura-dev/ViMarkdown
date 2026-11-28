using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViMarkdown;

internal static class WorkspacePaneChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var session = new EditorSession();
        session.Reset("ペインの幅調整");
        var window = session.Window;
        var grid = (Grid)window.Content!;
        var splitters = grid.Children.OfType<GridSplitter>().OrderBy(Grid.GetColumn).ToArray();
        var notes = grid.Children.Single(c => Grid.GetColumn(c) == 1);
        var pages = grid.Children.Single(c => Grid.GetColumn(c) == 3);
        var editor = grid.Children.Single(c => Grid.GetColumn(c) == 5);
        var initialNotes = notes.Bounds.Width;
        var initialPages = pages.Bounds.Width;

        Point Position(GridSplitter splitter) => splitter.TranslatePoint(new Point(2, 100), window)!.Value;
        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Drag(GridSplitter splitter, double delta)
        {
            var start = Position(splitter);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(delta, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(start + new Vector(delta, 0), MouseButton.Left);
            Layout();
        }
        void DoubleClick(GridSplitter splitter)
        {
            var point = Position(splitter);
            for (var i = 0; i < 2; i++)
            {
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
            }
            Layout();
        }
        void HeaderFits(Control pane)
        {
            foreach (var control in pane.GetVisualDescendants().OfType<Control>()
                         .Where(c => c is Button || c is ComboBox))
            {
                var origin = control.TranslatePoint(default, pane)!.Value;
                check(origin.X >= 0 && origin.X + control.Bounds.Width <= pane.Bounds.Width + .01,
                    "最小幅でもツールバーボタンとソート順がペイン内に収まる");
            }
        }
        void SearchFits()
        {
            var redo = editor.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "Redo"));
            var scope = editor.GetVisualDescendants().OfType<ComboBox>().Single(b => b.Name == "SearchScope");
            var keyword = editor.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "SearchKeyword");
            var search = editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SearchButton");
            double Left(Control control) => control.TranslatePoint(default, editor)!.Value.X;
            check(Left(scope) >= Left(redo) + redo.Bounds.Width &&
                keyword.Bounds.Width >= keyword.MinWidth && Left(keyword) >= 0 &&
                Left(keyword) + keyword.Bounds.Width <= editor.Bounds.Width + .01 &&
                Left(search) + search.Bounds.Width <= editor.Bounds.Width + .01,
                "最小幅でも検索対象はやり直しの右側にあり、キーワード欄と検索ボタンを全て表示する");
        }

        check(splitters.Length == 2 && grid.ColumnDefinitions[0].ActualWidth == 64,
            "幅調整はノート・ページとページ・エディタの境界のみ");
        Drag(splitters[0], -3000);
        check(notes.Bounds.Width >= 112 && notes.Bounds.Width < initialNotes, "ノートをドラッグで最小幅より狭められない");
        HeaderFits(notes);
        Drag(splitters[1], -3000);
        check(pages.Bounds.Width >= 320 && pages.Bounds.Width <= initialPages, "ページをドラッグで最小幅より狭められない");
        HeaderFits(pages);
        DoubleClick(splitters[0]);
        check(Math.Abs(notes.Bounds.Width - initialNotes) < .01 && Math.Abs(pages.Bounds.Width - 320) < .01,
            "ノート境界のダブルクリックはノートだけ初期幅に戻す");
        DoubleClick(splitters[1]);
        check(Math.Abs(pages.Bounds.Width - initialPages) < .01 && Math.Abs(notes.Bounds.Width - initialNotes) < .01,
            "ページ境界のダブルクリックはページを初期幅に戻す");
        Drag(splitters[1], 3000);
        check(editor.Bounds.Width >= 510 && editor.Bounds.Right <= grid.Bounds.Width + .01,
            "ページ拡大でもエディタの最小幅とウィンドウ内の配置を保持");
        var editorToolbar = editor.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "EditorToolbarScroll");
        check(editorToolbar.Bounds.Width <= editor.Bounds.Width && editorToolbar.Extent.Width <= editorToolbar.Viewport.Width + .01,
            "エディタが最小幅でも検索付きツールバーは横スクロールせず全て表示される");
        SearchFits();
        editorToolbar.Offset = new Vector(editorToolbar.Extent.Width - editorToolbar.Viewport.Width, 0);
        Layout();
        var searchButton = editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SearchButton");
        var searchOrigin = searchButton.TranslatePoint(default, editor)!.Value;
        check(searchOrigin.X >= 0 && searchOrigin.X + searchButton.Bounds.Width <= editor.Bounds.Width + .01,
            "横スクロールの末尾で検索ボタンがエディタ内に表示される");
        DoubleClick(splitters[1]);
        Drag(splitters[0], 3000);
        // One further drag verifies that reaching the limit is stable.
        Drag(splitters[0], 3000);
        check(editor.Bounds.Width >= 510 && editor.Bounds.Right <= grid.Bounds.Width + .01,
            "ノート拡大でもエディタがウィンドウ外へはみ出さない");
        Drag(splitters[0], -40);
        DoubleClick(splitters[0]);
        check(Math.Abs(notes.Bounds.Width - initialNotes) < .01, "拡大後もドラッグと初期幅への復元を繰り返せる");
        Drag(splitters[0], -3000);
        Drag(splitters[1], 3000);
        DoubleClick(splitters[0]);
        check(Math.Abs(notes.Bounds.Width - initialNotes) < .01 && editor.Bounds.Right <= grid.Bounds.Width + .01,
            "ページを最大まで拡大した後もノートを初期幅に戻せる");
        window.Width = window.MinWidth;
        Layout();
        check(notes.Bounds.Width >= 144 && pages.Bounds.Width >= 320 && editor.Bounds.Width >= 510 &&
              editor.Bounds.Right <= grid.Bounds.Width + .01,
            "ウィンドウ縮小時も各ペインの最小幅を守り画面内に収める");
        HeaderFits(notes);
        HeaderFits(pages);
        SearchFits();
        window.Height = window.MinHeight;
        Layout();
        var lastPeriod = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Timeline7");
        var trash = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TrashButton");
        var periodTop = lastPeriod.TranslatePoint(default, window)!.Value.Y;
        var trashTop = trash.TranslatePoint(default, window)!.Value.Y;
        check(window.MinHeight > 0 && trashTop >= periodTop + lastPeriod.Bounds.Height &&
            trashTop + trash.Bounds.Height <= window.ClientSize.Height + .01,
            $"最小高さでもごみ箱は先々月の下に収まり、ウィンドウ外へ隠れない: period={periodTop}+{lastPeriod.Bounds.Height}, trash={trashTop}+{trash.Bounds.Height}, client={window.ClientSize.Height}, min={window.MinHeight}");
        editor.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "SearchScope").SelectedIndex = 0;
        editor.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "SearchKeyword").Text = "ペイン";
        editor.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "SearchButton")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Layout();
        window.Width = window.MinWidth;
        Layout();
        SearchFits();
        check(grid.ColumnDefinitions[6].ActualWidth >= 250 && editor.Bounds.Width >= 510,
            "検索結果を開いた最小幅でも結果一覧と検索ツールバーの両方を表示する");
        window.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "CloseSearchResults")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Layout();
        var ring = pages.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PaneFocus2");
        var ringOrigin = ring.TranslatePoint(default, pages)!.Value;
        check(ringOrigin.X >= 2 && ringOrigin.X + ring.Bounds.Width <= pages.Bounds.Width - 2 + .01,
            "ページ一覧のフォーカス枠の左右がクリップされずペイン内に収まる");
        var pageList = pages.GetVisualDescendants().OfType<ListBox>().Single();
        pageList.ItemsSource = new[]
        {
            new WorkspaceItem(-1, null, "ABC123", false, "") { UpdatedAt = DateTimeOffset.UtcNow },
            new WorkspaceItem(-2, null, "日本語のページ", false, "") { UpdatedAt = DateTimeOffset.UtcNow },
            new WorkspaceItem(-3, null, "Mixed日本語123", false, "")
        };
        Layout();
        var rows = Enumerable.Range(0, 3).Select(i => (ListBoxItem)pageList.ContainerFromIndex(i)!).ToArray();
        check(rows.All(row => Math.Abs(row.Bounds.Height - rows[0].Bounds.Height) < .01),
            "英数字・日本語・混在タイトルでページ行の高さが揃う");
        var titleBaselines = rows.Select(row =>
        {
            var title = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name != "PageUpdatedAt");
            return title.TranslatePoint(default, row)!.Value.Y + title.TextLayout.TextLines[0].Baseline;
        }).ToArray();
        check(titleBaselines.All(y => Math.Abs(y - titleBaselines[0]) < .01),
            "英数字・日本語・混在タイトルの基準線を同じ位置に揃える");
        var datePositions = rows.Select(row => row.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Name == "PageUpdatedAt").TranslatePoint(default, row)!.Value.Y).ToArray();
        check(datePositions.All(y => Math.Abs(y - datePositions[0]) < .01),
            "文字種と更新日時の有無にかかわらず2段目の位置が揃う");
        foreach (var row in rows)
        {
            var labels = row.GetVisualDescendants().OfType<TextBlock>().ToArray();
            foreach (var label in labels)
            {
                // Compare against unconstrained font metrics, so a forced line
                // height cannot hide clipping by shrinking TextLayout too.
                using var natural = new TextLayout(label.Text ?? "", new Typeface(label.FontFamily),
                    label.FontSize, label.Foreground);
                var top = label.TranslatePoint(default, row)!.Value.Y;
                check(label.Bounds.Height + .01 >= natural.Height,
                    $"ページ一覧の{(label.Name == "PageUpdatedAt" ? "更新日時" : "タイトル")}はフォント本来の行高を確保する");
                check(top >= row.Padding.Top && top + natural.Height <= row.Bounds.Height - row.Padding.Bottom + .01,
                    "ページ一覧の文字全体が上下の余白の内側に収まる");
            }
            var title = labels.Single(t => t.Name != "PageUpdatedAt");
            var date = labels.Single(t => t.Name == "PageUpdatedAt");
            check(date.Margin.Top < 0 && Math.Abs(date.TranslatePoint(default, row)!.Value.Y -
                (title.TranslatePoint(default, row)!.Value.Y + title.Bounds.Height + 1 + date.Margin.Top)) < 1.01,
                "フォントの上下の空きを含めて段間隔を詰め、文字の行高は維持する");
        }
    }
}

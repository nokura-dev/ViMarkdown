using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViMarkdown;

internal static class WorkspaceSearchChecks
{
    public static void Run(Action<bool, string> check, string? renderPath)
    {
        var source = new[]
        {
            new WorkspaceItem(1, null, "ノート", true, ""),
            new WorkspaceItem(2, 1, "ページ", false, "# **Needle** needle\r\nなし\rneedle\n末尾"),
            new WorkspaceItem(3, null, "未分類ページ", false, "needle")
        };
        var found = WorkspaceSearch.Find(source, WorkspaceSearchScope.AllNotes, null, null, "needle");
        check(found.Count == 3 && found[1].LineNumber == 3, "CRLF・CR・LFの原文行番号で行ごとに検索する");
        check(found[0].LineText == "# **Needle** needle" && found[0].Offset == 4, "Markdown原文と一致語先頭のオフセットを保持する");
        check(WorkspaceSearch.Matches(found[0].LineText, "needle").Count == 2, "同じ行の全一致箇所を大小文字を区別せず検索する");
        check(WorkspaceSearch.Find(source, WorkspaceSearchScope.SelectedNote, 1, null, "needle").Count == 2,
            "選択ノートだけを検索する");
        check(WorkspaceSearch.Find(source, WorkspaceSearchScope.SelectedNote, null, null, "needle").Single().NoteName == "未分類",
            "未分類を選択した検索にも対応する");
        check(WorkspaceSearch.Find(source, WorkspaceSearchScope.CurrentPage, null, 2, "new", "new").Count == 1,
            "未保存本文を検索する");
        check(WorkspaceSearch.Matches("a.*b", ".*").Single() == (1, 3), "キーワードは正規表現でなく文字列として検索する");
        check(WorkspaceSearch.Matches("text", " ").Count == 0, "空白だけのキーワードは検索しない");

        var path = Path.Combine(Path.GetTempPath(), "workspace-search-" + Guid.NewGuid() + ".db");
        long note, page;
        using (var store = new WorkspaceStore(path))
        {
            note = store.Create("開発メモ", true, null);
            var other = store.Create("仕様書", true, null);
            page = store.Create("検索機能", false, note,
                "# 検索機能\n" + string.Join("\n", Enumerable.Range(1, 600).Select(i => $"row{i:D4} **needle** と needle を検索する。")));
            store.Create("操作ガイド", false, other, "別ノートの needle");
            store.Create("未分類ページ", false, null, "未分類の needle");
            store.SetSetting("initialized", "1"); store.SetSetting("active", page.ToString());
        }
        try
        {
            using var session = new EditorSession(path, height: 480);
            var window = session.Window;
            T Named<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
            var scope = Named<ComboBox>("SearchScope");
            var keyword = Named<TextBox>("SearchKeyword");
            var results = Named<ListBox>("SearchResults");
            var pane = Named<Border>("SearchResultsPane");
            void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
            void Search(string text, int index, bool enter = false)
            {
                scope.SelectedIndex = index;
                keyword.Text = text;
                keyword.Focus();
                if (enter) session.Key(PhysicalKey.Enter);
                else Named<Button>("SearchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Layout();
            }
            check(scope.SelectedIndex == 2 && !pane.IsVisible, "初期値は表示中のページで結果ペインは閉じている");
            keyword.Focus();
            session.Key(PhysicalKey.Tab, RawInputModifiers.Shift);
            check(scope.IsKeyboardFocusWithin, "検索キーワードのShift+Tabで検索範囲へフォーカスを移す");
            var expandModifier = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Alt;
            var otherModifier = OperatingSystem.IsMacOS() ? RawInputModifiers.Alt : RawInputModifiers.Meta;
            session.Key(PhysicalKey.ArrowDown, otherModifier);
            check(!scope.IsDropDownOpen, "リスト展開はOSに対応する修飾キーだけで行う");
            session.Key(PhysicalKey.ArrowDown, expandModifier);
            Layout();
            check(scope.IsDropDownOpen, "MacはCommand+下矢印、WindowsはAlt+下矢印で候補を展開する");
            session.Key(PhysicalKey.ArrowUp);
            session.Key(PhysicalKey.Enter);
            Layout();
            check(!scope.IsDropDownOpen && scope.SelectedIndex == 1, "上矢印で候補を移動しEnterで確定する");
            session.Key(PhysicalKey.ArrowDown, expandModifier);
            session.Key(PhysicalKey.ArrowDown);
            session.Key(PhysicalKey.Enter);
            Layout();
            check(!scope.IsDropDownOpen && scope.SelectedIndex == 2, "下矢印でも候補を移動してEnterで確定する");
            session.Key(PhysicalKey.ArrowDown, expandModifier);
            session.Key(PhysicalKey.ArrowUp);
            session.Key(PhysicalKey.Escape);
            Layout();
            check(!scope.IsDropDownOpen && scope.SelectedIndex == 2, "Escapeはリストを閉じて確定前の選択を保持する");
            session.Key(PhysicalKey.Tab);
            check(keyword.IsKeyboardFocusWithin, "検索範囲のTabで検索キーワードへ戻る");
            Search("needle", 2, true);
            check(!pane.IsVisible && session.Rich.Caret == session.Editor.Text.IndexOf("needle", StringComparison.Ordinal),
                "Enterで表示中ページの一致語先頭へ直接移動する");
            check(session.Rich.SearchRanges.Count == 1200, "本文内の全一致語をハイライトする");
            var first = session.Rich.Caret;
            Search("needle", 2, true);
            check(session.Rich.Caret > first, "同じページの再検索で次の一致語へ進む");
            Search("row0600", 2, true);
            var documentScroll = session.Rich.GetVisualAncestors().OfType<ScrollViewer>().First();
            var caretBounds = session.Rich.CaretBounds;
            check(documentScroll.Offset.Y > 0 && caretBounds.Top >= documentScroll.Offset.Y &&
                caretBounds.Bottom <= documentScroll.Offset.Y + documentScroll.Viewport.Height,
                "表示外の該当行へスクロールしカーソルを画面内に表示する");
            Search("needle", 1);
            check(results.IsKeyboardFocusWithin && results.SelectedIndex == 0,
                "検索ボタンで実行すると結果一覧へ移動し先頭を選択する");
            var ring = Named<Border>("PaneFocus4");
            var cards = Named<Border>("SearchResultsCards");
            void CheckResultLayout()
            {
                var row = results.ContainerFromIndex(0)!;
                var firstTop = row.TranslatePoint(default, cards)!.Value.Y;
                var cardOrigin = cards.TranslatePoint(default, ring.GetVisualParent()!)!.Value;
                var summary = pane.GetVisualDescendants().OfType<TextBlock>()
                    .Single(t => t.Text?.StartsWith("「needle」") == true);
                var summaryTop = summary.TranslatePoint(default, ring.GetVisualParent()!)!.Value.Y;
                check(summaryTop >= 8 && cardOrigin.Y > summaryTop && ring.Bounds.Height > cards.Bounds.Height,
                    "フォーカス枠は検索件数と結果一覧を囲み、ツールバーを含めない");
                check(Math.Abs(firstTop - 8) < 1 && row.TranslatePoint(default, ring.GetVisualParent()!)!.Value.X >= 8,
                    "先頭カードとフォーカス枠の間に一定の余白を保つ");
                check(ring.BorderBrush is Avalonia.Media.ISolidColorBrush active && active.Color == Avalonia.Media.Color.Parse("#007ACC"),
                    "検索実行直後から結果ペインの青枠を表示する");
            }
            CheckResultLayout();
            for (var i = 0; i < 15; i++) session.Key(PhysicalKey.ArrowDown);
            for (var i = 0; i < 15; i++) session.Key(PhysicalKey.ArrowUp);
            Layout();
            CheckResultLayout();
            check(results.GetVisualDescendants().OfType<ScrollViewer>().Single().Offset.Y == 0,
                "先頭へ戻ると余白を維持しスクロール位置も上端へ戻す");
            check(results.Items.Count == 600 && pane.IsVisible, "選択ノート検索で600行のカードを表示する");
            session.Click("NORMAL (Esc)");
            session.Keys("/");
            keyword.Text = ""; keyword.Focus();
            window.KeyTextInput("needle");
            check(keyword.Text == "needle", "Vim検索プロンプト中もツールバー検索欄へ文字を入力できる");
            session.Key(PhysicalKey.Enter);
            Layout();
            check(results.IsKeyboardFocusWithin && results.SelectedIndex == 0,
                "検索欄のEnterでも結果一覧の先頭へフォーカスを移す");
            Search("needle", 0);
            check(results.Items.Count == 602, "全ノート検索には別ノートと未分類のページも含む");
            var scroll = results.GetVisualDescendants().OfType<ScrollViewer>().Single();
            check(scroll.Extent.Height > scroll.Viewport.Height * 10, "多数の結果に縦スクロール範囲がある");
            foreach (var fraction in new[] { 0d, .25, .5, .75, 1, .5, 0, 1 })
            {
                scroll.Offset = new Vector(0, (scroll.Extent.Height - scroll.Viewport.Height) * fraction);
                Layout();
                var rows = results.GetVisualDescendants().OfType<ListBoxItem>()
                    .Where(r => r.DataContext is WorkspaceSearchHit && r.Bounds.Height > 0).ToArray();
                check(rows.Length > 0 && rows.Length < 100, "スクロール中もカードを仮想化して表示する");
                foreach (var row in rows)
                {
                    var hit = (WorkspaceSearchHit)row.DataContext!;
                    var texts = row.GetVisualDescendants().OfType<TextBlock>().ToArray();
                    check(texts.Length == 2 && texts[0].Text == $"{hit.NoteName} → {hit.PageName}　{hit.LineNumber}行目",
                        "再利用されたカードも正しいノート・ページ・行番号を2段で表示する");
                    var runs = texts[1].Inlines!.OfType<Run>().ToArray();
                    check(string.Concat(runs.Select(r => r.Text)) == hit.LineText && runs.Count(r => r.Background is not null) == WorkspaceSearch.Matches(hit.LineText, hit.Keyword).Count,
                        "スクロール後もMarkdown原文と一致語のハイライトが正しく表示される");
                    check(row.Bounds.Width <= results.Bounds.Width && row.Bounds.Height >= 40, "カードはペイン内に収まり2段の高さを保持する");
                }
            }
            var lastHit = results.Items.Cast<WorkspaceSearchHit>().Last();
            var lastRow = results.ContainerFromIndex(results.ItemCount - 1)!;
            check(Equals(lastRow.DataContext, lastHit) && scroll.Offset.Y > 0, "末尾までスクロールして最後のカードを表示できる");
            var beforeScroll = scroll.Offset.Y;
            var point = lastRow.TranslatePoint(new Point(25, lastRow.Bounds.Height / 2), window)!.Value;
            for (var i = 0; i < 2; i++)
            {
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
            }
            Layout();
            check(pane.IsVisible && results.Items.Count == 602 && Math.Abs(scroll.Offset.Y - beforeScroll) < 2,
                "ダブルクリックで移動した後も結果一覧とスクロール位置を保持する");
            check(window.Title == "ViMarkdown — " + lastHit.PageName && session.Rich.Caret == lastHit.Offset && session.Rich.SearchRanges.Count > 0,
                "画面外だった末尾カードのダブルクリックで該当ページ・行・一致語先頭に移動する");
            if (renderPath is not null)
            {
                Search("needle", 0);
                Layout();
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
                bitmap.Render(window); bitmap.Save(renderPath);
            }
            window.Width = window.MinWidth;
            Layout();
            check(pane.Bounds.Width >= 250 && pane.Bounds.Right <= window.Bounds.Width + 1,
                "最小ウィンドウ幅でも結果ペインを画面内に表示する");
            Named<Button>("CloseSearchResults").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout();
            check(!pane.IsVisible && ((Grid)window.Content!).ColumnDefinitions[6].ActualWidth == 0,
                "閉じるボタンで結果列を畳みエディタ幅を戻す");
            Search("一致しない語句", 0);
            check(pane.IsVisible && results.ItemCount == 0 && results.IsKeyboardFocusWithin, "0件でも空の結果ペインを表示してフォーカスを移す");
            Search("needle", 2);
            check(!pane.IsVisible, "表示中ページの検索では結果ペインを開かない");
            session.Click("INSERT (i)");
            session.Editor.Document.Text = "未保存の内容\nnew keyword";
            Search("keyword", 2, true);
            check(session.Editor.CaretOffset == session.Editor.Text.IndexOf("keyword", StringComparison.Ordinal) && session.Editor.SelectionLength == 0,
                "挿入モードでも未保存本文の一致語先頭へカーソルを移動し選択状態にしない");
            session.Editor.Document.Text = "変更後";
            check(session.Rich.SearchRanges.Count == 0, "本文変更時に古い位置のハイライトを消す");
            foreach (var (mode, origin) in new[]
            {
                ("NORMAL (Esc)", "results"), ("INSERT (i)", "keyword"), ("VISUAL (v)", "editor")
            })
            {
                session.Click(mode);
                Search("変更", 0);
                check(pane.IsVisible && results.ItemCount > 0, "Ctrl+F検証用の検索結果を表示する");
                if (origin == "keyword") keyword.Focus();
                if (origin == "editor") session.Click(mode);
                var position = session.Editor.IsVisible ? session.Editor.CaretOffset : session.Rich.Caret;
                session.Key(PhysicalKey.F, RawInputModifiers.Control);
                Layout();
                check(!pane.IsVisible && ((Grid)window.Content!).ColumnDefinitions[6].ActualWidth == 0 &&
                    (session.Editor.IsVisible ? session.Editor.TextArea.IsKeyboardFocusWithin : session.Rich.IsKeyboardFocusWithin),
                    "結果表示中のCtrl+Fは各フォーカス位置・編集モードからペインを閉じて編集へ戻る");
                check((session.Editor.IsVisible ? session.Editor.CaretOffset : session.Rich.Caret) == position,
                    "Ctrl+Fで戻る際も編集位置を保持する");
                session.Key(PhysicalKey.F, RawInputModifiers.Control);
                check(keyword.IsKeyboardFocusWithin, "結果が非表示ならCtrl+Fは従来どおり検索欄へ移動する");
            }
            Search("no-match-unique", 0);
            session.Key(PhysicalKey.F, RawInputModifiers.Control);
            Layout();
            check(!pane.IsVisible && session.Rich.IsKeyboardFocusWithin, "0件の結果ペインもCtrl+Fで閉じて編集へ戻る");
        }
        finally
        {
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*")) File.Delete(file);
        }
    }
}

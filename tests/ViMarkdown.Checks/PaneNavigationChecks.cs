using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using ViMarkdown;

internal static class PaneNavigationChecks
{
    public static void Run(Action<bool, string> check)
    {
        CheckLists(check);
        CheckScrolling(check);
        using var session = new EditorSession();
        var window = session.Window;
        var grid = (Grid)window.Content!;
        var notes = grid.Children.Single(c => Grid.GetColumn(c) == 1).GetVisualDescendants().OfType<ListBox>().Single();
        var pages = grid.Children.Single(c => Grid.GetColumn(c) == 3).GetVisualDescendants().OfType<ListBox>().Single();
        check(!grid.Children.Single(c => Grid.GetColumn(c) == 0).GetVisualDescendants().OfType<ScrollViewer>().Any(),
            "期間ペインにはスクロールバーがなく、同じ空き領域の問題はない");
        T Named<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        void Move(bool right) => session.Key(right ? PhysicalKey.ArrowRight : PhysicalKey.ArrowLeft,
            RawInputModifiers.Control | RawInputModifiers.Shift);
        void Ring(int index)
        {
            var rings = window.GetVisualDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("PaneFocus") == true);
            check(rings.Count(b => b.BorderBrush is ISolidColorBrush brush && brush.Color.A > 0) == 1 &&
                Named<Border>("PaneFocus" + index).BorderBrush is ISolidColorBrush active && active.Color == Color.Parse("#007ACC"),
                "フォーカスのある内容領域だけに青枠を表示する");
        }
        session.Click("INSERT (i)");
        session.Editor.CaretOffset = 5;
        session.Editor.Select(3, 2);
        var caret = session.Editor.CaretOffset;
        Ring(3);
        Move(false);
        check(pages.IsKeyboardFocusWithin, "編集からページへ移動する");
        Ring(2);
        Move(true);
        check(session.Editor.TextArea.IsKeyboardFocusWithin && session.Editor.CaretOffset == caret && session.Editor.SelectionLength == 2,
            "挿入モードのカーソル・選択を維持して復帰する");
        Move(true);
        check(session.Editor.TextArea.IsKeyboardFocusWithin, "検索結果が非表示なら右端で止まる");
        Move(false); Move(false);
        check(notes.IsKeyboardFocusWithin, "ノートへ移動する");
        Move(false); Ring(0);
        check(Named<Button>("Timeline0").IsKeyboardFocusWithin, "期間の先頭へ移動する");
        Move(false);
        check(Named<Button>("Timeline0").IsKeyboardFocusWithin, "左端で循環しない");
        session.Key(PhysicalKey.ArrowDown);
        check(Named<Button>("Timeline1").IsKeyboardFocusWithin, "上下キーで期間ボタンへ移動する");
        check(Named<Button>("Timeline1").Background is ISolidColorBrush selected && selected.Color == Color.Parse("#CDE8FF"),
            "下キーで期間の選択も切り替える");
        Move(true); Move(false);
        check(Named<Button>("Timeline1").IsKeyboardFocusWithin, "期間のフォーカス位置を復元する");
        session.Key(PhysicalKey.ArrowDown);
        session.Key(PhysicalKey.ArrowDown);
        check(pages.ItemCount == 0 && notes.ItemCount == 0 && Named<Button>("Timeline3").IsKeyboardFocusWithin,
            "上下キーで先週を選ぶと期間外のページ・ノートを絞り込む");
        session.Key(PhysicalKey.ArrowUp);
        check(pages.ItemCount > 0 && notes.ItemCount > 0, "今週へ戻ると絞り込み結果も更新する");
        session.Key(PhysicalKey.ArrowUp);
        session.Key(PhysicalKey.ArrowUp);
        check(Named<Button>("Timeline0").Background is ISolidColorBrush all && all.Color == Color.Parse("#CDE8FF"),
            "上キーでも期間を選択する");
        Move(true); Move(true);
        session.Key(PhysicalKey.ArrowDown);
        check(pages.IsKeyboardFocusWithin, "一覧の上下移動は一覧にフォーカスを保つ");
        Move(true);
        session.Click("NORMAL (Esc)");
        session.Key(PhysicalKey.L);
        var richCaret = session.Rich.Caret;
        Move(false); Move(true);
        check(session.Rich.IsKeyboardFocusWithin && session.Rich.Caret == richCaret, "ノーマルモードの編集位置へ復帰する");
        session.Click("VISUAL (v)");
        session.Key(PhysicalKey.L);
        richCaret = session.Rich.Caret;
        Move(false); Move(true);
        check(session.Rich.Caret == richCaret, "選択モードでも移動前の位置へ復帰する");
        session.Click("NORMAL (Esc)");
        Named<TextBox>("SearchKeyword").Text = "操作";
        Named<ComboBox>("SearchScope").SelectedIndex = 0;
        Named<Button>("SearchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        session.Rich.Focus();
        Move(true);
        var results = Named<ListBox>("SearchResults");
        check(results.IsKeyboardFocusWithin, "表示された検索結果へ移動できる");
        Ring(4);
        Move(true);
        check(results.IsKeyboardFocusWithin, "検索結果の右端で循環しない");
        var nextResult = Math.Min(results.SelectedIndex + 1, results.ItemCount - 1);
        session.Key(PhysicalKey.ArrowDown);
        check(results.SelectedIndex == nextResult && results.IsKeyboardFocusWithin, "検索結果を上下で選択しても本文へ飛ばない");
        session.Key(PhysicalKey.Enter);
        check(session.Rich.IsKeyboardFocusWithin, "検索結果をEnterで開く");
        Move(true);
        check(results.SelectedIndex == nextResult && results.IsKeyboardFocusWithin, "検索結果の選択へ復帰する");
        Named<TextBox>("SearchKeyword").Text = "no-match-unique";
        Named<Button>("SearchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        session.Rich.Focus();
        Move(true);
        session.Key(PhysicalKey.ArrowDown);
        check(results.ItemCount == 0 && results.IsKeyboardFocusWithin, "空の検索結果にも移動できる");
        Named<TextBox>("SearchKeyword").Focus();
        check(Named<Border>("PaneFocus4").BorderBrush == Brushes.Transparent, "ツールバーへ移ると内容の青枠を消す");
    }
    private static void CheckLists(Action<bool, string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "pane-navigation-" + Guid.NewGuid() + ".db");
        try
        {
            using (var store = new WorkspaceStore(path))
            {
                var note = store.Create("Aノート", true, null);
                var other = store.Create("Bノート", true, null);
                var first = store.Create("Aページ", false, note, "最初の本文");
                store.Create("Bページ", false, note, "次の本文");
                store.Create("Cページ", false, other, "別ノートの本文");
                store.SetSetting("initialized", "1");
                store.SetSetting("active", first.ToString());
            }
            using var session = new EditorSession(path);
            var grid = (Grid)session.Window.Content!;
            var pages = grid.Children.Single(c => Grid.GetColumn(c) == 3).GetVisualDescendants().OfType<ListBox>().Single();
            var notes = grid.Children.Single(c => Grid.GetColumn(c) == 1).GetVisualDescendants().OfType<ListBox>().Single();
            void Left() => session.Key(PhysicalKey.ArrowLeft, RawInputModifiers.Control | RawInputModifiers.Shift);
            void StablePadding(ListBox list, string label)
            {
                session.Window.UpdateLayout();
                double Top() => list.ContainerFromIndex(0)!.TranslatePoint(default, list)!.Value.Y;
                var initial = Top();
                for (var i = 0; i < 20; i++)
                {
                    session.Key(PhysicalKey.ArrowDown);
                    check(Math.Abs(Top() - initial) < 1, $"{label}: 下キーで上部余白が増えない (初期={initial}, 現在={Top()})");
                    session.Key(PhysicalKey.ArrowUp);
                    check(Math.Abs(Top() - initial) < 1, label + ": 上下の反復で上部余白を保つ");
                }
            }
            Left();
            StablePadding(pages, "短いページ一覧");
            session.Key(PhysicalKey.ArrowDown);
            check(pages.SelectedIndex == 1 && session.Editor.Text == "次の本文" && pages.IsKeyboardFocusWithin,
                "ページの上下選択で内容を開き一覧にフォーカスを保つ");
            session.Key(PhysicalKey.ArrowUp);
            check(pages.SelectedIndex == 0 && session.Editor.Text == "最初の本文", "上キーで前ページへ戻る");
            Left();
            session.Key(PhysicalKey.ArrowDown);
            check(notes.SelectedItem?.ToString() == "Bノート" && pages.ItemCount == 1 && notes.IsKeyboardFocusWithin,
                "ノートの上下選択でページ一覧を切り替える");
            session.Window.KeyTextInput("x");
            check(session.Editor.Text == "最初の本文", "一覧への文字入力で本文を編集しない");
            session.Key(PhysicalKey.ArrowUp);
            check(notes.SelectedItem?.ToString() == "Aノート" && pages.ItemCount == 2 && notes.IsKeyboardFocusWithin,
                "上キーで前ノートへ戻る");
            StablePadding(notes, "短いノート一覧");
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
        }
    }

    private static void CheckScrolling(Action<bool, string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "pane-scrolling-" + Guid.NewGuid() + ".db");
        try
        {
            using (var store = new WorkspaceStore(path))
            {
                var first = 0L;
                for (var n = 0; n < 35; n++)
                {
                    var note = store.Create($"ノート{n:D2}", true, null);
                    for (var p = 0; p < (n == 0 ? 35 : 1); p++)
                    {
                        var id = store.Create($"ページ{n:D2}-{p:D2}", false, note, "needle 本文");
                        if (first == 0) first = id;
                    }
                }
                store.SetSetting("initialized", "1");
                store.SetSetting("active", first.ToString());
            }
            using var session = new EditorSession(path, height: 480);
            var window = session.Window;
            var grid = (Grid)window.Content!;
            ListBox Pane(int column) => grid.Children.Single(c => Grid.GetColumn(c) == column)
                .GetVisualDescendants().OfType<ListBox>().Single();
            T Named<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
            void Move(bool right) => session.Key(right ? PhysicalKey.ArrowRight : PhysicalKey.ArrowLeft,
                RawInputModifiers.Control | RawInputModifiers.Shift);
            void Layout() { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
            double Top(ListBox list) => list.ContainerFromIndex(0)!.TranslatePoint(default, list)!.Value.Y;
            void Cycle(ListBox list, string label)
            {
                Layout();
                var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
                var top = Top(list);
                ScrollChecks.CheckThumbAtTop(scroll, check, label + " 初期表示");
                for (var cycle = 0; cycle < 15; cycle++)
                {
                    session.Key(PhysicalKey.ArrowDown);
                    session.Key(PhysicalKey.ArrowUp);
                }
                Layout();
                check(Math.Abs(Top(list) - top) < 1,
                    $"{label}: 短い上下移動を繰り返しても上部余白を保つ (初期={top}, 現在={Top(list)}, offset={scroll.Offset})");
                for (var i = list.SelectedIndex; i < list.ItemCount - 1; i++) session.Key(PhysicalKey.ArrowDown);
                check(scroll.Offset.Y > 0, label + ": 末尾までスクロールできる");
                scroll.ScrollToEnd();
                Layout();
                ScrollChecks.CheckThumbAtBottom(scroll, check, label);
                var last = list.ContainerFromIndex(list.SelectedIndex)!;
                var position = last.TranslatePoint(default, list)!.Value;
                check(position.Y >= 0 && position.Y + last.Bounds.Height <= list.Bounds.Height + 1,
                    label + ": 選択した末尾の項目が欠けずに表示される");
                for (var i = list.SelectedIndex; i > 0; i--) session.Key(PhysicalKey.ArrowUp);
                Layout();
                ScrollChecks.CheckThumbAtTop(scroll, check, label);
                check(list.SelectedIndex == 0 && Math.Abs(scroll.Offset.Y) < .1 && Math.Abs(Top(list) - top) < 1,
                    $"{label}: 先頭へ戻るとスクロールと余白を復元 (初期={top}, 現在={Top(list)}, offset={scroll.Offset})");
                ScrollChecks.CheckActivity(window, scroll, check, label, drag: label == "ページ一覧");
            }
            Move(false);
            Cycle(Pane(3), "ページ一覧");
            Move(false);
            // Include the uncategorized row in the baseline.
            session.Key(PhysicalKey.ArrowUp);
            Cycle(Pane(1), "ノート一覧");
            Named<TextBox>("SearchKeyword").Text = "needle";
            Named<ComboBox>("SearchScope").SelectedIndex = 0;
            Named<Button>("SearchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            session.Rich.Focus();
            Move(true);
            session.Key(PhysicalKey.ArrowDown);
            Cycle(Named<ListBox>("SearchResults"), "検索結果");
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
        }
    }

}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViMarkdown;

internal static class FocusChecks
{
    public static void Run(Action<bool, string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "markdown-focus-" + Guid.NewGuid() + ".db");
        using (var store = new WorkspaceStore(path))
        {
            var first = store.Create("A", false, null, "本文ABC");
            store.Create("B", false, null, "別ページDEF");
            store.Create("別のノート", true, null);
            store.SetSetting("initialized", "1");
            store.SetSetting("active", first.ToString());
        }
        using var session = new EditorSession(path);
        var window = session.Window;
        try
        {
            var editor = session.Editor;
            var rich = session.Rich;
            void ClickPage(bool other = false)
            {
                var list = window.GetVisualDescendants().OfType<ListBox>()
                    .Single(l => l.SelectedItem is WorkspaceItem);
                var row = list.GetVisualDescendants().OfType<ListBoxItem>()
                    .First(r => other ? !r.IsSelected : r.IsSelected);
                var point = row.TranslatePoint(new Point(15, row.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
            }
            var toolbarButtons = window.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Name != "TrashButton" && b.Name != "ActivateDetachedEditor" && b.Name?.StartsWith("Timeline") != true).ToArray();
            check(toolbarButtons.Length >= 10 && toolbarButtons.All(b => !b.Focusable && !b.IsTabStop && !b.Focus()),
                "各ツールバーの操作ボタンはTab・方向キー・明示的なFocusでもフォーカスを受け取らない");
            var pages = ((Grid)window.Content!).Children.Single(c => Grid.GetColumn(c) == 3)
                .GetVisualDescendants().OfType<ListBox>().Single();
            var notes = ((Grid)window.Content!).Children.Single(c => Grid.GetColumn(c) == 1)
                .GetVisualDescendants().OfType<ListBox>().Single();
            foreach (var modifiers in new[] { RawInputModifiers.None, RawInputModifiers.Shift })
            {
                notes.Focus();
                for (var n = 0; n < 20; n++)
                {
                    session.Key(PhysicalKey.Tab, modifiers);
                    check(toolbarButtons.All(b => !b.IsKeyboardFocusWithin), "TabとShift+Tabでツールバーボタンに入らない");
                }
            }
            session.Key(PhysicalKey.F, RawInputModifiers.Control);
            check(window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SearchKeyword").IsKeyboardFocusWithin,
                "Ctrl+Fによる検索欄への明示的な移動は維持する");
            var createNote = toolbarButtons.Single(b => b.Tag?.ToString() == "ノートを作成");
            var buttonPoint = createNote.TranslatePoint(new Point(10, 10), window)!.Value;
            notes.Focus();
            window.MouseDown(buttonPoint, MouseButton.Left);
            window.MouseUp(buttonPoint, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            check(window.OwnedWindows.Single().Title == "ノートを作成", "フォーカス対象外でもマウスでツールバー操作ができる");
            var dialog = window.OwnedWindows.Single();
            dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            check(!window.OwnedWindows.Any() && !createNote.IsKeyboardFocusWithin && notes.IsKeyboardFocusWithin,
                "ダイアログを閉じるとツールバーではなく元の一覧へ復帰する");
            foreach (var index in new[] { 1, 0 })
            {
                var noteRow = notes.ContainerFromIndex(index)!;
                var notePoint = noteRow.TranslatePoint(new Point(15, 10), window)!.Value;
                window.MouseDown(notePoint, MouseButton.Left);
                window.MouseUp(notePoint, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                check(notes.IsKeyboardFocusWithin &&
                    window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PaneFocus1").BorderBrush is ISolidColorBrush { Color: var color } && color == Color.Parse("#007ACC"),
                    "ノートの切替と一覧再構築後もフォーカス枠が追従する");
            }
            for (var i = 0; i < 3; i++)
            {
                window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "PageSort").SelectedIndex = i;
                Dispatcher.UIThread.RunJobs();
                // Returning from another note leaves the page unselected.
                // Select a page explicitly before testing editor focus.
                if (pages.SelectedIndex < 0) pages.SelectedIndex = 0;
                Dispatcher.UIThread.RunJobs();
                session.Click("INSERT (i)", focus: true);
                session.Click("INSERT (i)", focus: true);
                check(editor.TextArea.IsKeyboardFocusWithin, "挿入モードを再クリックしてもTextAreaにフォーカスが戻る");
                ClickPage(other: true);
                check(pages.IsKeyboardFocusWithin && editor.CaretOffset == 0,
                    "別ページをクリックすると一覧にフォーカスを保持する");
                ClickPage();
                check(pages.IsKeyboardFocusWithin, "選択済みページのクリックでも一覧にフォーカスを保持する");
                check(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PaneFocus2").BorderBrush is ISolidColorBrush { Color: var pageColor } && pageColor == Color.Parse("#007ACC"),
                    "ページクリック後もページ一覧のフォーカス枠が表示される");
                session.Click("INSERT (i)");
                var before = editor.Text;
                var offset = editor.CaretOffset;
                window.KeyTextInput("入力");
                check(editor.Text == before.Insert(offset, "入力"), "挿入モードの再選択後も文字入力できる");
                session.Key(PhysicalKey.Escape);
                check(!editor.IsVisible && rich.IsKeyboardFocusWithin, "Esc後は表示中のリッチ本文にフォーカスが戻る");
                ClickPage(other: true);
                check(pages.IsKeyboardFocusWithin, "ノーマルモードのページクリックでも一覧にフォーカスを保持する");
                session.Click("VISUAL (v)", focus: true);
                session.Click("VISUAL (v)", focus: true);
                ClickPage();
                check(pages.IsKeyboardFocusWithin, "選択モードのページクリックでも一覧にフォーカスを保持する");
                session.Click("VISUAL (v)");
                var caret = rich.Caret;
                session.Key(PhysicalKey.L);
                check(rich.Caret > caret, "選択モードの再選択後も移動キーが効く");
                session.Click("NORMAL (Esc)", focus: true);
                session.Key(PhysicalKey.I);
                check(editor.IsVisible && editor.TextArea.IsKeyboardFocusWithin, "ノーマルボタンからiで挿入モードへ移行できる");
            }
        }
        finally
        {
            window.Close();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                File.Delete(path + suffix);
        }
    }
}

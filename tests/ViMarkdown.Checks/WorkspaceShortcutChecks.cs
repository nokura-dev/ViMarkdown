using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

internal static class WorkspaceShortcutChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var session = new EditorSession();
        var window = session.Window;
        void Save() => typeof(ViMarkdown.MainWindow).GetMethod("SaveCurrent",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null);
        var existingTitle = window.Title;
        session.Editor.Document.Text = "2026/11/20 10:00\n# 既存ページは変更しない";
        Save();
        check(window.Title == existingTitle, "既存ページは日時と2行目の見出しがあっても自動命名しない");
        Window Dialog(string title)
        {
            var dialog = window.OwnedWindows.Single();
            check(dialog.Title == title, "ショートカットで「" + title + "」を開く");
            check(dialog.SystemDecorations == SystemDecorations.None && !dialog.ShowInTaskbar,
                "OS標準のタイトルバーとタスクバー表示を使わない");
            var suffix = title.EndsWith("作成") ? "作成" : title == "削除の確認" ? "削除" : title == "移動先を選択" ? "移動" : "名前変更";
            var candidates = window.GetVisualDescendants().OfType<Button>().Where(b => b.Tag?.ToString()?.EndsWith("を" + suffix) == true);
            check(candidates.Any(b => dialog.Position == b.PointToScreen(new Point(0, b.Bounds.Height + 4))),
                "対応する操作ボタンの直下に表示する");
            return dialog;
        }
        void PressButton(Window dialog, string label)
        {
            dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }
        void Create(PhysicalKey key, RawInputModifiers modifiers, string title, string name)
        {
            session.Key(key, modifiers);
            var dialog = Dialog(title);
            var input = dialog.GetVisualDescendants().OfType<TextBox>().Single();
            check(!dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "作成")).IsEnabled,
                "空の名前では作成できない");
            input.Text = "   ";
            check(!dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "作成")).IsEnabled,
                "空白だけの名前では作成できない");
            (double Height, double Baseline) MeasureName(string text)
            {
                input.Text = text;
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var textPresenter = input.GetVisualDescendants().OfType<TextPresenter>().Single();
                return (input.Bounds.Height, textPresenter.TranslatePoint(default, input)!.Value.Y +
                    textPresenter.TextLayout.TextLines[0].Baseline);
            }
            var japanese = MeasureName("名前Ag");
            var latin = MeasureName("ABC123");
            check(Math.Abs(japanese.Height - latin.Height) < .01 && Math.Abs(japanese.Baseline - latin.Baseline) < .01,
                "ダイアログの入力欄は日本語を基準に英数字の高さとベースラインを揃える");
            input.Text = name;
            PressButton(dialog, "作成");
        }
        Create(PhysicalKey.N, RawInputModifiers.Control | RawInputModifiers.Shift, "ノートを作成", "ショートカットのノート");
        session.Key(PhysicalKey.N, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        var lines = session.Editor.Text.Split('\n');
        check(!window.OwnedWindows.Any() && lines.Length == 2 && lines[1] == "",
            "ページは名前入力なしで日時・空行の2行構成で作成する");
        check(DateTime.TryParseExact(lines[0], "yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var created) && Math.Abs((DateTime.Now - created).TotalSeconds) < 65 &&
            window.Title == "ViMarkdown — " + lines[0], "作成日時を1行目とページ名へ設定する");
        check(session.Editor.IsVisible && session.Editor.TextArea.IsKeyboardFocusWithin &&
            session.Editor.Document.GetLocation(session.Editor.CaretOffset) == new AvaloniaEdit.Document.TextLocation(2, 1),
            "新規ページを2行目先頭にカーソルを置いた挿入モードで開く");
        session.Editor.Document.Text = lines[0] + "\n  ##   ";
        Save();
        check(window.Title == "ViMarkdown — " + lines[0], "2行目が空白や見出し記号のみなら日時名を維持する");
        session.Editor.Document.Text = lines[0] + "\n  ##   新しいページ名  ";
        Save();
        check(window.Title == "ViMarkdown — 新しいページ名", "2行目の見出し記号と前後空白を除いた名前に自動変更する");
        session.Editor.Document.Text += "続き";
        Save();
        check(window.Title == "ViMarkdown — 新しいページ名  続き", "入力途中の保存後も2行目の入力に名前が追従する");
        session.Editor.Document.Text = "# 1行目のタイトル\n別の本文";
        Save();
        check(window.Title == "ViMarkdown — 1行目のタイトル", "日時行をタイトルへ置き換えた場合は1行目を採用する");
        session.Editor.Document.Text = lines[0] + "\n## 2行目のタイトル";
        Save();
        check(window.Title == "ViMarkdown — 2行目のタイトル", "1行目を日時に戻した場合も2行目を採用する");
        foreach (var second in new[] { "", "   ", "  ##   " })
        {
            session.Editor.Document.Text = lines[0] + "\n" + second;
            Save();
            check(window.Title == "ViMarkdown — " + lines[0],
                "2行目を無効な文字列に戻すと以前の名前ではなく1行目の日時を採用する");
        }
        session.Editor.Document.Text = lines[0];
        Save();
        check(window.Title == "ViMarkdown — " + lines[0], "2行目がない日時だけのページは1行目を採用する");
        session.Editor.Document.Text = "# 日付ではない1行目\n## 使わない2行目";
        Save();
        check(window.Title == "ViMarkdown — 日付ではない1行目", "1行目が日付以外なら有効な2行目より1行目を優先する");
        session.Editor.Document.Text = lines[0] + "\n## 2行目のタイトル";
        Save();
        var pageList = ((Grid)window.Content!).Children.Single(c => Grid.GetColumn(c) == 3)
            .GetVisualDescendants().OfType<ListBox>().Single();
        var createdPage = (ViMarkdown.WorkspaceItem)pageList.SelectedItem!;
        var open = typeof(ViMarkdown.MainWindow).GetMethod("OpenPage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        open.Invoke(window, new object?[] { null });
        open.Invoke(window, new object?[] { createdPage.Id });
        session.Editor.Document.Text = lines[0] + "\n# 再表示後は変更しない";
        Save();
        check(window.Title == "ViMarkdown — 2行目のタイトル", "新規作成ページも一度離れて開き直した後は自動命名しない");
        session.Key(PhysicalKey.N, RawInputModifiers.Control);
        session.Key(PhysicalKey.F2);
        var rename = Dialog("名前変更");
        var nameInput = rename.GetVisualDescendants().OfType<TextBox>().Single();
        nameInput.Text = "Enterで保存";
        var presenter = nameInput.GetVisualDescendants().OfType<TextPresenter>().Single();
        presenter.PreeditText = "にほん";
        rename.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        rename.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        check(rename.IsVisible, "IME変換中のEnterではダイアログを確定しない");
        presenter.PreeditText = null;
        rename.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        check(!rename.IsVisible && window.Title == "ViMarkdown — Enterで保存", "Enterで名前変更を確定する");
        session.Editor.Document.Text = lines[0] + "\n# 自動変更しない";
        Save();
        check(window.Title == "ViMarkdown — Enterで保存", "手動で名前変更した後は2行目による自動変更を停止する");
        session.Key(PhysicalKey.F2);
        rename = Dialog("名前変更");
        rename.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        check(!rename.IsVisible, "Escapeでダイアログを閉じる");
        // Restore the name used by the remaining shortcut checks.
        session.Key(PhysicalKey.F2);
        rename = Dialog("名前変更");
        rename.GetVisualDescendants().OfType<TextBox>().Single().Text = "ショートカットのページ";
        PressButton(rename, "保存");
        foreach (var mode in new[] { "NORMAL (Esc)", "INSERT (i)", "VISUAL (v)" })
        {
            session.Click(mode);
            session.Key(PhysicalKey.F, RawInputModifiers.Control);
            var keyword = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SearchKeyword");
            check(keyword.IsKeyboardFocusWithin, "Ctrl+Fは各編集モードからキーワード欄へフォーカスを移す");
            check(keyword.CaretIndex == (keyword.Text?.Length ?? 0), "Ctrl+Fは検索欄の末尾へカーソルを移す");
            session.Key(PhysicalKey.F, RawInputModifiers.Control | RawInputModifiers.Shift);
            var scope = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "SearchScope");
            check(keyword.IsKeyboardFocusWithin && scope.SelectedIndex == 1,
                "Ctrl+Shift+Fは選択中のノートを検索範囲に設定して入力欄へ移る");
            keyword.Text = "自動変更";
            session.Key(PhysicalKey.Enter);
            var results = window.GetVisualDescendants().OfType<ListBox>().Single(c => c.Name == "SearchResults");
            check(results.IsKeyboardFocusWithin, "ノート検索後は結果一覧にフォーカスする");
            session.Key(PhysicalKey.Escape);
            check(!window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "SearchResultsPane").IsVisible &&
                keyword.Text == "" && (session.Editor.IsKeyboardFocusWithin || session.Rich.IsKeyboardFocusWithin),
                "検索結果のEscでパネルとキーワードをクリアしてエディタへ戻る");
            session.Key(PhysicalKey.F2, RawInputModifiers.Control);
            var dialog = Dialog("名前変更");
            check(dialog.GetVisualDescendants().OfType<TextBox>().Single().Text == "ショートカットのノート",
                "Ctrl+F2は各編集モードから選択ノートを対象にする");
            PressButton(dialog, "キャンセル");
            session.Key(PhysicalKey.F2);
            dialog = Dialog("名前変更");
            check(dialog.GetVisualDescendants().OfType<TextBox>().Single().Text == "ショートカットのページ",
                "F2は各編集モードから選択ページを対象にする");
            PressButton(dialog, "キャンセル");
            session.Key(PhysicalKey.M, RawInputModifiers.Control);
            PressButton(Dialog("移動先を選択"), "キャンセル");
        }
        session.Click("INSERT (i)");
        session.Editor.Document.Text = "ABC";
        session.Editor.CaretOffset = 0;
        session.Key(PhysicalKey.Delete);
        check(session.Editor.Text == "BC" && !window.OwnedWindows.Any(), "本文のDeleteは文字を削除する");
        var grid = (Grid)window.Content!;
        session.Click("NORMAL (Esc)");
        session.Keys("/");
        session.Key(PhysicalKey.N, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        check(!window.OwnedWindows.Any() && session.Editor.IsVisible && session.Editor.CaretOffset == session.Editor.Text.Length,
            "検索コマンド中のCtrl+Nでも新規ページを挿入モードで開く");
        session.Key(PhysicalKey.Escape);
        var notes = grid.Children.Single(c => Grid.GetColumn(c) == 1)
            .GetVisualDescendants().OfType<ListBox>().Single();
        notes.SelectedIndex = 0;
        window.UpdateLayout();
        notes.ContainerFromIndex(0)!.Focus();
        session.Key(PhysicalKey.Delete);
        session.Key(PhysicalKey.F2, RawInputModifiers.Control);
        check(!window.OwnedWindows.Any(), "未分類の削除・名前変更はショートカットでも実行しない");
        notes.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        foreach (var column in new[] { 3, 1 })
        {
            var list = grid.Children.Single(c => Grid.GetColumn(c) == column)
                .GetVisualDescendants().OfType<ListBox>().Single();
            list.SelectedIndex = column == 1 ? 1 : 0;
            window.UpdateLayout();
            var count = list.ItemCount;
            list.ContainerFromIndex(list.SelectedIndex)!.Focus();
            session.Key(PhysicalKey.Delete);
            Dispatcher.UIThread.RunJobs();
            check(!window.OwnedWindows.Any() && list.ItemCount == count - 1,
                "Deleteで確認ダイアログなしに選択項目を削除する");
        }
    }
}

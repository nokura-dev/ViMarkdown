using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using ViMarkdown;

internal static class WorkspaceChecks
{
    public static void Run(Action<bool, string> check)
    {
        var today = new DateTime(2026, 1, 1);
        DateTimeOffset Local(int year, int month, int day) => new(new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Local));
        check(TimelinePeriods.Matches(Local(2025,12,29), 3, today) && !TimelinePeriods.Matches(Local(2025,12,28), 3, today), "今週は年をまたいでも月曜から");
        check(TimelinePeriods.Matches(Local(2025,12,28), 4, today) && !TimelinePeriods.Matches(Local(2025,12,29), 4, today), "先週は月曜から日曜");
        check(TimelinePeriods.Matches(Local(2026,1,1), 5, today) && !TimelinePeriods.Matches(Local(2026,1,2), 5, today), "今月は今日まで");
        check(TimelinePeriods.Matches(Local(2025,12,31), 6, today) && TimelinePeriods.Matches(Local(2025,11,1), 7, today), "先月と先々月は年をまたぐ");
        check(TimelinePeriods.Matches(Local(2024,2,29), 6, new DateTime(2024,3,1)), "閏日の先月判定");
        check(!TimelinePeriods.Matches(Local(2025,12,31), 1, today) && TimelinePeriods.Matches(Local(2026,1,1), 1, today), "今日だけを選択");
        var names = new[] { "ページ10", "ページ2", "ページ1", "ページ999999999999999999999999", "ページ20" };
        var ordered = names.Select((name, index) => new WorkspaceItem(index, null, name, false, "") { UpdatedAt = DateTimeOffset.UnixEpoch.AddDays(index) }).ToArray();
        check(WorkspaceOrdering.ByName(ordered).Select(i => i.Name).SequenceEqual(new[] { "ページ1", "ページ2", "ページ10", "ページ20", "ページ999999999999999999999999" }), "自然順は桁数の大きな数値も数値順に比較");
        check(NaturalNameComparer.Instance.Compare("Page02", "page2") == 0 && NaturalNameComparer.Instance.Compare("a2b3", "a2b10") < 0, "自然順は英字の大小と先頭ゼロを同一視し複数の数値を比較");
        check(WorkspaceOrdering.Pages(ordered, PageSortOrder.UpdatedNewest).Select(i => i.Id).SequenceEqual(new long[] { 4, 3, 2, 1, 0 }) && WorkspaceOrdering.Pages(ordered, PageSortOrder.UpdatedOldest).Select(i => i.Id).SequenceEqual(new long[] { 0, 1, 2, 3, 4 }), "更新日時の新しい順と古い順");
        var path = Path.Combine(Path.GetTempPath(), "markdown-workspace-" + Guid.NewGuid() + ".db");
        try
        {
            using (var legacy = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
            {
                legacy.Open();
                using var command = legacy.CreateCommand();
                command.CommandText = "CREATE TABLE items(id INTEGER PRIMARY KEY, parent_id INTEGER REFERENCES items(id) ON DELETE CASCADE, name TEXT NOT NULL, is_folder INTEGER NOT NULL, body TEXT NOT NULL DEFAULT ''); CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT); INSERT INTO items VALUES(100,NULL,'旧親',1,''),(101,100,'旧子',1,''),(102,101,'旧ページ',0,'保持本文'); INSERT INTO settings VALUES('folder','101');";
                command.ExecuteNonQuery();
            }
            using (var migrated = new WorkspaceStore(path))
            {
                check(migrated.Items().Where(i => i.IsNote).All(i => i.NoteId is null) && migrated.Items().Single(i => i.Id == 102).NoteId == 101 && migrated.Items().Single(i => i.Id == 102).Body == "保持本文", "旧階層を平坦化してページの所属と本文を保持");
                check(migrated.GetSetting("note") == "101", "旧選択設定をノートへ移行");
                check(migrated.Items().All(i => i.UpdatedAt == DateTimeOffset.MinValue), "既存データの更新日時は不明として移行");
                migrated.Delete(100);
                check(migrated.Items().Count == 2, "旧親を削除しても独立した旧子ノートとページを保持");
                migrated.Delete(101);
            }
            long note, child, page;
            using (var store = new WorkspaceStore(path))
            {
                note = store.Create("親", true, null); child = store.Create("子", true, null);
                page = store.Create("ページ", false, child, "本文 👩‍💻\r\n");
                store.Rename(page, "変更名");
                var beforeMove = store.Items().Single(i => i.Id == page).UpdatedAt;
                store.MovePage(page, note);
                check(store.Items().Single(i => i.Id == page).UpdatedAt == beforeMove,
                    "別のノートへ移動しても元の更新日時を保持する");
                store.Save(page, "保存本文\n日本語");
                bool blocked = false; try { store.MovePage(note, child); } catch (InvalidOperationException) { blocked = true; }
                check(blocked && store.Items().Single(i => i.Id == note).NoteId is null, "ノートの移動を拒否し平坦な一覧を維持");
                blocked = false; try { store.MovePage(page, page); } catch (InvalidOperationException) { blocked = true; }
                check(blocked, "ページを移動先ノートに指定できない");
                blocked = false; try { store.Create("入れ子", true, note); } catch (InvalidOperationException) { blocked = true; }
                check(blocked, "ノートの入れ子作成を拒否");
                store.SetSetting("initialized", "1"); store.SetSetting("active", page.ToString());
            }
            using (var store = new WorkspaceStore(path))
            {
                var item = store.Items().Single(i => i.Id == page);
                check(item.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-1), "更新日時をDB再接続後も保持");
                store.Save(page, item.Body); store.Rename(page, item.Name); store.MovePage(page, item.NoteId);
                check(store.Items().Single(i => i.Id == page).UpdatedAt == item.UpdatedAt, "内容が変わらない操作では更新日時を変更しない");
                check(item.Name == "変更名" && item.NoteId == note && item.Body == "保存本文\n日本語", "DBを開き直して名前・移動先・本文を復元");
            }
            var window = new MainWindow(path); window.Show(); Dispatcher.UIThread.RunJobs();
            var editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
            check(editor.Text == "保存本文\n日本語", "起動時に最後のページと本文を復元");
            var lists = window.GetVisualDescendants().OfType<ListBox>().Where(list => list.ContextMenu is not null).ToList();
            check(lists.Count == 2 && lists.All(list => list.ContextMenu?.Items.OfType<MenuItem>()
                .Select(item => item.Header?.ToString()).TakeLast(3).SequenceEqual(new[] { "md出力", "docx出力", "xlsx出力" }) == true),
                "ノート一覧とページ一覧にMD・docx・xlsx出力メニュー");
            var notesList = lists.Single(list => list.Items.Cast<object>().Any(item => item.ToString() == "子"));
            var emptyNoteIndex = notesList.Items.Cast<object>().ToList().FindIndex(item => item.ToString() == "子");
            var emptyNoteContainer = notesList.ContainerFromIndex(emptyNoteIndex)!;
            foreach (var menuItem in notesList.ContextMenu!.Items.OfType<MenuItem>().TakeLast(3))
            {
                emptyNoteContainer.RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent });
                check(!menuItem.IsEnabled, "ページがない右クリックノートではMD・docx・xlsx出力を無効にする");
                notesList.ContextMenu.Close();
            }
            // Right-clicking the empty note selects it and clears the active page.
            notesList.SelectedItem = notesList.Items.Cast<object>().Single(item => item.ToString() == "親");
            var pagesList = lists.Single(list => list != notesList);
            pagesList.SelectedItem = pagesList.Items.Cast<WorkspaceItem>().Single(item => item.Id == page);
            Dispatcher.UIThread.RunJobs();
            editor.Document.Text = "自動保存の確認 👩‍💻";
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                Dispatcher.UIThread.MainLoop(timeout.Token);
            using (var store = new WorkspaceStore(path)) check(store.Items().Single(i => i.Id == page).Body == editor.Text, "入力後のタイマーでSQLiteへ自動保存");
            editor.Document.Text = "終了直前の変更"; window.Close();
            var restored = new MainWindow(path); restored.Show(); Dispatcher.UIThread.RunJobs();
            check(restored.GetVisualDescendants().OfType<TextEditor>().Single().Text == "終了直前の変更", "待機時間前の終了でも保存し再起動で復元");
            long second;
            using (var store = new WorkspaceStore(path)) second = store.Create("別ページ", false, note, "別の本文");
            restored.Close();
            var switching = new MainWindow(path); switching.Show(); Dispatcher.UIThread.RunJobs();
            var source = switching.GetVisualDescendants().OfType<TextEditor>().Single();
            source.Document.Text = "切り替え直前";
            var list = switching.GetVisualDescendants().OfType<ListBox>().Single(l => l.ItemsSource is List<WorkspaceItem>);
            list.SelectedItem = ((List<WorkspaceItem>)list.ItemsSource!).Single(i => i.Id == second);
            check(source.Text == "別の本文" && !source.Document.UndoStack.CanUndo, "ページ切り替えで本文を読み込みUndo履歴を分離");
            using (var store = new WorkspaceStore(path)) check(store.Items().Single(i => i.Id == page).Body == "切り替え直前", "ページ切り替え時に待機中の変更を即時保存");
            var sort = switching.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "PageSort");
            sort.SelectedIndex = 1;
            check(((List<WorkspaceItem>)list.ItemsSource!)[0].Id == page && ((WorkspaceItem)list.SelectedItem!).Id == second && source.Text == "別の本文", "更新順へ切り替えても選択ページと編集内容を保持");
            sort.SelectedIndex = 2;
            check(((List<WorkspaceItem>)list.ItemsSource!)[0].Id == second, "プルダウンで更新の古い順へ切り替え");
            sort.SelectedIndex = 1;
            source.Document.Text = "更新順で切り替え直前";
            list.SelectedItem = ((List<WorkspaceItem>)list.ItemsSource!).Single(i => i.Id == page);
            check(((WorkspaceItem)list.SelectedItem!).Id == page && source.Text == "切り替え直前", "更新順で未保存ページから切り替えても選択表示と本文が一致");
            switching.Close();
            var sortedRestart = new MainWindow(path); sortedRestart.Show(); Dispatcher.UIThread.RunJobs();
            check(sortedRestart.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "PageSort").SelectedIndex == 1, "ページ表示順の設定を再起動後も復元");
            sortedRestart.Close();
            using (var store = new WorkspaceStore(path))
            {
                store.Delete(note); check(store.Items().Count == 1 && store.Items()[0].Id == child, "ノート削除は所属ページのみ削除し別ノートを保持");
                store.Delete(child);
            }
            var empty = new MainWindow(path); empty.Show(); Dispatcher.UIThread.RunJobs();
            check(empty.GetVisualDescendants().OfType<TextEditor>().Single().Text == "", "全削除後の再起動でサンプルを再作成しない"); empty.Close();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
        }
    }
}

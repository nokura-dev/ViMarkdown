using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViMarkdown;
using Microsoft.Data.Sqlite;

internal static class TrashChecks
{
    public static void Run(Action<bool, string> check)
    {
        using (var store = new WorkspaceStore(":memory:"))
        {
            var note = store.Create("配下削除", true, null);
            var page = store.Create("配下ページ", false, note, "配下本文");
            var updated = store.Items().Single(i => i.Id == page).UpdatedAt;
            store.Delete(note);
            check(store.Items().Count == 0 && store.TrashItems().Single() is var trash &&
                trash.Id == page && trash.Body == "配下本文" && trash.UpdatedAt == updated && trash.DeletedAt.HasValue,
                "ノート削除時に配下のページを更新日を変えずごみ箱へ移す");
        }
        var path = Path.Combine(Path.GetTempPath(), "markdown-trash-" + Guid.NewGuid() + ".db");
        try
        {
            long page, note;
            DateTimeOffset updated, deleted;
            using (var store = new WorkspaceStore(path))
            {
                note = store.Create("ノート", true, null);
                page = store.Create("削除ページ", false, note, "保持する本文");
                updated = store.Items().Single(i => i.Id == page).UpdatedAt;
                store.Delete(page);
                var trash = store.TrashItems().Single();
                deleted = trash.DeletedAt!.Value;
                check(store.Items().All(i => i.Id != page) && trash.Body == "保持する本文" && trash.UpdatedAt == updated,
                    "削除ページは通常一覧から消え、本文と更新日を保持する");
                store.Delete(page);
                check(store.TrashItems().Single().DeletedAt == deleted, "重複削除では保持期限を延長しない");
                store.PurgeExpired(deleted.AddDays(30).AddTicks(-1));
                check(store.TrashItems().Count == 1, "30日経過直前は保持する");
            }
            using (var store = new WorkspaceStore(path))
            {
                check(store.TrashItems().Single().DeletedAt == deleted, "再接続後も削除日を保持する");
                store.Restore(page);
                check(store.Items().Single(i => i.Id == page) is var restored && restored.NoteId == note && restored.UpdatedAt == updated && restored.DeletedAt is null,
                    "復元では所属と更新日を保持する");
                store.Delete(page);
                store.Delete(note);
                check(store.TrashItems().Single().NoteId is null, "所属ノート削除後もごみ箱のページを保持する");
                store.Restore(page);
                check(store.Items().Single().NoteId is null, "削除されたノートのページは未分類へ復元する");
                store.Delete(page);
                var cutoff = store.TrashItems().Single().DeletedAt!.Value.AddDays(30);
                store.PurgeExpired(cutoff);
                check(store.TrashItems().Count == 0, "30日経過ちょうどで完全削除する");
                page = store.Create("期限切れ", false, null);
                store.Delete(page);
                store.SetSetting("initialized", "1");
            }
            using (var connection = new SqliteConnection("Data Source=" + path))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE items SET deleted_at=$date";
                command.Parameters.AddWithValue("$date", DateTimeOffset.UtcNow.AddDays(-31).Ticks);
                command.ExecuteNonQuery();
            }
            using (var store = new WorkspaceStore(path))
            {
                check(store.TrashItems().Count == 0, "起動時に期限切れを自動削除する");
                page = store.Create("復元対象", false, null, "本文プレビュー");
                store.Delete(page);
            }
            var window = new MainWindow(path);
            window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TrashButton");
                var workspace = (Grid)window.Content!;
                var timeline = workspace.Children.Single(c => Grid.GetColumn(c) == 0);
                check(button.Bounds.Height > 0 && button.Content is Avalonia.Controls.Shapes.Path &&
                    timeline.GetVisualDescendants().Contains(button) && Grid.GetRow(button) == 1,
                    "タイムラインの最下部にごみ箱アイコンを表示する");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
                var dialog = window.OwnedWindows.Single();
                check(dialog.GetVisualDescendants().OfType<TextBox>().Single().Text == "本文プレビュー", "ごみ箱で本文を確認できる");
                dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "復元")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                using var store = new WorkspaceStore(path);
                check(store.TrashItems().Count == 0 && store.Items().Single().Id == page, "ごみ箱の復元ボタンでページを復元する");
                dialog.Close();
            }
            finally { window.Close(); }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
        }
    }
}

using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Microsoft.Data.Sqlite;
using ViMarkdown;

internal static class DetachedEditorChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Flags)!.GetValue(target)!;
    private static object? Call(MainWindow window, string name, params object?[] args)
        => typeof(MainWindow).GetMethod(name, Flags)!.Invoke(window, args);
    private static Dictionary<long, MainWindow> Windows(MainWindow window)
        => Field<Dictionary<long, MainWindow>>(window, "_detachedEditors");
    private static TextEditor Editor(MainWindow window) => Field<TextEditor>(window, "_editor");
    private static void Select(MainWindow window, long id)
    {
        Call(window, "OpenPage", id);
        Call(window, "ReloadLists", true);
        Dispatcher.UIThread.RunJobs();
    }
    private static MainWindow Open(MainWindow window, long id)
    {
        Call(window, "OpenDetachedEditor", id);
        Dispatcher.UIThread.RunJobs();
        return Windows(window)[id];
    }

    public static void Run(Action<bool, string> check)
    {
        using (var session = new EditorSession())
        {
            var main = session.Window;
            var store = Field<WorkspaceStore>(main, "_store");
            var a = store.Create("ページA", false, null, "Aの本文");
            var b = store.Create("ページB", false, null, "Bの本文");
            Select(main, a);
            var first = Open(main, a);
            check(Editor(first).Text == "Aの本文" && first.Title == "ViMarkdown — ページA",
                "別窓に対象ページの本文とタイトルを表示する");
            check(!session.Editor.IsEnabled && Field<Border>(main, "_detachedPageNotice").IsVisible,
                "メインの同じページは編集を止め案内を表示する");
            check(!first.GetVisualDescendants().Contains(Field<ListBox>(first, "_pages")),
                "別窓はページ一覧を持たずエディタだけを表示する");
            first.WindowState = WindowState.Minimized;
            check(ReferenceEquals(Open(main, a), first) && Windows(main).Count == 1 && first.WindowState == WindowState.Normal,
                "再度開くと窓を増やさず最小化を解除する");

            Select(main, b);
            check(session.Editor.IsEnabled && session.Editor.Text == "Bの本文",
                "メインで別ページを編集できる");
            var second = Open(main, b);
            Editor(first).Text = "Aの更新本文";
            Editor(second).Text = "Bの更新本文";
            check((bool)Call(main, "SaveAllEditors")! && store.Items().Single(p => p.Id == a).Body == "Aの更新本文" &&
                store.Items().Single(p => p.Id == b).Body == "Bの更新本文",
                "異なるページの別窓を同時に開きそれぞれの本文を保存する");

            Select(main, a);
            var actions = Field<Dictionary<(bool, string), Func<Task>>>(main, "_workspaceActions");
            var rename = actions[(false, "名前変更")]();
            Dispatcher.UIThread.RunJobs();
            var dialog = main.OwnedWindows.Single();
            dialog.GetVisualDescendants().OfType<TextBox>().Single().Text = "改名したA";
            dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "保存"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            rename.GetAwaiter().GetResult();
            check(first.Title == "ViMarkdown — 改名したA" && main.Title == first.Title,
                "メインからの改名を別窓のタイトルにも反映する");

            Field<TextBox>(main, "_searchKeyword").Text = "更新";
            Call(main, "RunWorkspaceSearch");
            var hit = Field<ListBox>(main, "_searchResults").Items.OfType<WorkspaceSearchHit>().Single();
            Call(main, "NavigateToSearchHit", hit, false);
            Dispatcher.UIThread.RunJobs();
            check(hit.PageId == a && Field<int>(first, "_caret") == 2 && !session.Editor.IsEnabled,
                "メインの検索は別窓の本文を検索し既存窓の一致箇所へ移動する");
            Field<TextBox>(first, "_searchKeyword").Text = "更新";
            Call(first, "RunWorkspaceSearch");
            check(Field<ListBox>(first, "_searchResults").Items.OfType<WorkspaceSearchHit>().All(h => h.PageId == a),
                "別窓の検索はそのページ内に限定する");

            first.Close();
            Dispatcher.UIThread.RunJobs();
            check(!Windows(main).ContainsKey(a) && session.Editor.IsEnabled && session.Editor.Text == "Aの更新本文" &&
                !Field<Border>(main, "_detachedPageNotice").IsVisible,
                "別窓を閉じるとメインで保存済み本文の編集を再開する");
            check(store.Create("接続保持", false, null) > 0, "別窓の終了では共有DBを破棄しない");

            first = Open(main, a);
            Editor(first).Text = "削除直前の本文";
            actions[(false, "削除")]().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            check(!first.IsVisible && !Windows(main).ContainsKey(a) && store.TrashItems().Single(p => p.Id == a).Body == "削除直前の本文",
                "削除前に別窓を保存し対象窓を閉じる");
            check(second.IsVisible && Editor(second).Text == "Bの更新本文", "別ページの削除では他の窓を保持する");
            main.Close();
            check(!main.IsVisible && !second.IsVisible && Windows(main).Count == 0,
                "メイン終了時にすべてのエディタを閉じる");
        }
        CheckSaveFailure(check);
    }

    private static void CheckSaveFailure(Action<bool, string> check)
    {
        using var session = new EditorSession();
        var main = session.Window;
        var store = Field<WorkspaceStore>(main, "_store");
        var id = store.Create("保存失敗", false, null, "保存前");
        Select(main, id);
        var child = Open(main, id);
        var connection = Field<SqliteConnection>(store, "_connection");
        void Execute(string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        Execute($"CREATE TRIGGER reject_editor_save BEFORE UPDATE OF body ON items WHEN OLD.id={id} BEGIN SELECT RAISE(ABORT, '保存失敗の検証'); END;");
        try
        {
            Editor(child).Text = "失敗しても保持する本文";
            child.Close();
            check(child.IsVisible && Windows(main).ContainsKey(id) && Editor(child).Text == "失敗しても保持する本文",
                "保存失敗時は別窓を閉じず編集内容を保持する");
            main.Close();
            check(main.IsVisible && child.IsVisible, "別窓の保存失敗時はメインの終了も止める");
            Field<Dictionary<(bool, string), Func<Task>>>(main, "_workspaceActions")[(false, "削除")]().GetAwaiter().GetResult();
            check(store.Items().Any(p => p.Id == id) && child.IsVisible, "別窓の保存失敗時は削除も止める");
        }
        finally { Execute("DROP TRIGGER reject_editor_save;"); }
        child.Close();
        check(!child.IsVisible && session.Editor.Text == "失敗しても保持する本文" &&
            store.Items().Single(p => p.Id == id).Body == "失敗しても保持する本文",
            "保存障害を解消すれば再保存してメインへ戻れる");
    }
}

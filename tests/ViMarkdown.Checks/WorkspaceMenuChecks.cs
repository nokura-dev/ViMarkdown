using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViMarkdown;
using System.Reflection;

internal static class WorkspaceMenuChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var session = new EditorSession();
        var window = session.Window;
        var grid = (Grid)window.Content!;
        ListBox List(int column) => grid.Children.Single(c => Grid.GetColumn(c) == column)
            .GetVisualDescendants().OfType<ListBox>().Single();
        var notes = List(1);
        var pages = List(3);
        MenuItem[] Items(ListBox list) => list.ContextMenu!.Items.OfType<MenuItem>().ToArray();
        void Click(MenuItem item)
        {
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }
        void Cancel(Window dialog)
        {
            dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "キャンセル"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }
        void Context(ListBox list, bool row, int index = 0)
        {
            window.UpdateLayout();
            var point = row
                ? list.ContainerFromIndex(index)!.TranslatePoint(new Point(15, 15), window)!.Value
                : list.TranslatePoint(new Point(20, list.Bounds.Height - 20), window)!.Value;
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            list.ContextMenu!.Close();
        }
        check(Items(notes).Select(i => i.Header).SequenceEqual(new[] { "ノート作成", "名前変更", "削除", "md出力", "docx出力" }),
            "ノートの右クリックメニューを指定順で表示する");
        check(Items(pages).Select(i => i.Header).SequenceEqual(new[] { "ページ作成", "名前変更", "ノート移動", "削除", "md出力", "docx出力" }),
            "ページの右クリックメニューを指定順で表示する");
        foreach (var list in new[] { notes, pages })
        {
            var entries = list.ContextMenu!.Items.ToArray();
            var deleteIndex = Array.FindIndex(entries, entry => entry is MenuItem { Header: "削除" });
            check(entries.OfType<Separator>().Count() == 2 &&
                entries[deleteIndex - 1] is Separator && entries[deleteIndex + 1] is Separator,
                "削除の前後を区切り、基本操作・削除・出力をグループ化する");
            Context(list, false);
            check(Items(list)[0].IsEnabled && Items(list).Skip(1).All(i => !i.IsEnabled),
                "一覧の空白では作成のみ有効にする");
            Click(Items(list)[0]);
            if (list == notes)
            {
                var dialog = window.OwnedWindows.Single();
                check(dialog.Title == "ノートを作成", "ノート作成メニューで名前入力を開く");
                Cancel(dialog);
            }
            else check(!window.OwnedWindows.Any() && session.Editor.Text.EndsWith("\n") && session.Editor.Document.LineCount == 2 && session.Editor.IsVisible,
                "ページ作成メニューで名前入力なしにテンプレートを挿入モードで開く");
        }
        Context(notes, true);
        check(!Items(notes)[1].IsEnabled && !Items(notes)[2].IsEnabled && Items(notes)[3].IsEnabled && Items(notes)[4].IsEnabled,
            "未分類は名前変更と削除を無効にし出力は有効にする");
        Context(pages, true);
        check(Items(pages).All(i => i.IsEnabled), "ページ上では全メニューが有効になる");
        foreach (var index in new[] { 1, 2 })
        {
            Click(Items(pages)[index]);
            var dialog = window.OwnedWindows.Single();
            check(dialog.Title == (index == 1 ? "名前変更" : "移動先を選択"),
                "ページの名前変更・ノート移動を既存ダイアログへ紐付ける");
            Cancel(dialog);
        }
        foreach (var body in new[] { "", " \n\t", "本文あり", "" })
        {
            session.Editor.Document.Text = body;
            Context(pages, true);
            var enabled = !string.IsNullOrWhiteSpace(body);
            check(Items(pages).TakeLast(2).All(i => i.IsEnabled == enabled),
                "未保存の最新本文に従ってMD・DOCX出力の有効状態を切り替える");
            check(Items(pages).Take(4).All(i => i.IsEnabled), "本文が空でも出力以外のページ操作は有効にする");
        }
        Context(notes, true);
        check(Items(notes).TakeLast(2).All(i => i.IsEnabled),
            "本文が空でもページが存在するノートの出力は有効にする");

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var store = (WorkspaceStore)typeof(MainWindow).GetField("_store", flags)!.GetValue(window)!;
        var populatedNote = store.Create("A 本文あり", true, null);
        var emptyNote = store.Create("B ページなし", true, null);
        var fullPage = store.Create("A 本文あり", false, populatedNote, "本文");
        var emptyPage = store.Create("B 本文なし", false, populatedNote, "");
        typeof(MainWindow).GetMethod("ReloadLists", flags)!.Invoke(window, null);
        Dispatcher.UIThread.RunJobs();
        Context(notes, true, 1);
        check(Items(notes).All(i => i.IsEnabled), "未選択の通常ノートを右クリックすると操作と出力が有効になる");
        check(pages.Items.OfType<WorkspaceItem>().Any(p => p.Id == fullPage), "右クリックしたノートのページを表示する");
        Context(pages, true, 0);
        check(Items(pages).All(i => i.IsEnabled), "未選択の本文ありページを右クリックすると出力が有効になる");
        Context(pages, true, 1);
        check(Items(pages).Take(4).All(i => i.IsEnabled) && Items(pages).TakeLast(2).All(i => !i.IsEnabled),
            "本文なしページへ右クリックで切り替えると出力だけ無効になる");
        Context(pages, true, 0);
        check(Items(pages).All(i => i.IsEnabled), "本文ありページへ右クリックで戻ると出力が有効になる");
        Context(notes, true, 2);
        check(Items(notes).Take(3).All(i => i.IsEnabled) && Items(notes).TakeLast(2).All(i => !i.IsEnabled),
            "ページのないノートへ右クリックで切り替えると出力だけ無効になる");
        Context(notes, true, 0);
        check(!Items(notes)[1].IsEnabled && !Items(notes)[2].IsEnabled && Items(notes).TakeLast(2).All(i => i.IsEnabled),
            "未分類へ右クリックで切り替えると名前変更・削除だけ無効になる");

    }
}

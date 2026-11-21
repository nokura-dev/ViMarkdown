using System.Reflection;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ViMarkdown;

internal static class PageMoveChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var session = new EditorSession();
        var window = session.Window;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        var store = (WorkspaceStore)Field("_store")!;
        var note = store.Create("移動先", true, null);
        var page = store.Create("移動するページ", false, null, "保存前");
        Call("OpenPage", page);
        Call("ReloadLists");
        session.Editor.Document.Text = "移動前の編集内容";
        check((bool)Call("MovePageToNote", page, note)!, "別のノートへ移動できる");
        var moved = store.Items().Single(i => i.Id == page);
        check(moved.NoteId == note && moved.Body == "移動前の編集内容", "移動時に編集内容を保存する");
        check((long?)Field("_activePageId") == page && (long?)Field("_noteId") == note,
            "移動したページを開いたまま移動先ノートを選択する");
        var pages = (ListBox)Field("_pages")!;
        check((pages.SelectedItem as WorkspaceItem)?.Id == page, "移動先の一覧でページの選択を保持する");
        check(!(bool)Call("MovePageToNote", page, note)!, "同じノートへの移動は変更しない");
        check((bool)Call("MovePageToNote", page, null)!, "未分類へ移動できる");
        check(store.Items().Single(i => i.Id == page).UpdatedAt == moved.UpdatedAt,
            "保存済みページの移動では更新日時を保持する");
        check(store.Items().Single(i => i.Id == page).NoteId is null && Field("_noteId") is null,
            "未分類への移動を一覧と保存先へ反映する");

        var ids = Enumerable.Range(0, 5).Select(i => store.Create("範囲" + i, false, null, "本文" + i)).ToArray();
        Call("OpenPage", ids[2]);
        Call("ReloadLists");
        session.Editor.CaretOffset = 2;
        long[] Selected() => pages.SelectedItems!.Cast<WorkspaceItem>().Select(p => p.Id).Order().ToArray();
        void Expect(long[] expected, string message) => check(Selected().SequenceEqual(expected.Order()), message);
        void Click(long id, bool shift = false, bool release = true)
        {
            var index = pages.Items.Cast<WorkspaceItem>().ToList().FindIndex(p => p.Id == id);
            pages.ScrollIntoView(index);
            window.UpdateLayout();
            var point = pages.ContainerFromIndex(index)!.TranslatePoint(new Point(15, 15), window)!.Value;
            var modifiers = shift ? RawInputModifiers.Shift : RawInputModifiers.None;
            window.MouseDown(point, MouseButton.Left, modifiers);
            if (release) window.MouseUp(point, MouseButton.Left, modifiers);
            Dispatcher.UIThread.RunJobs();
        }
        Click(ids[4], true);
        Expect(ids[2..5], "Shiftクリックで起点から末尾まで選択する");
        check((long?)Field("_activePageId") == ids[2] && session.Editor.Document.Text == "本文2" && session.Editor.CaretOffset == 2,
            "範囲選択中は起点ページの本文と編集位置を保持する");
        session.Key(PhysicalKey.ArrowUp, RawInputModifiers.Shift);
        Expect(ids[2..4], "Shift上キーはクリックした範囲端から縮小する");
        session.Key(PhysicalKey.ArrowUp, RawInputModifiers.Shift);
        session.Key(PhysicalKey.ArrowUp, RawInputModifiers.Shift);
        Expect(ids[1..3], "Shift上キーで起点を越えて逆側へ選択を伸ばす");
        Call("ReloadLists");
        Expect(ids[1..3], "一覧更新後も範囲を保持する");
        session.Key(PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Expect([ids[2]], "一覧更新後も範囲端から選択を縮小する");
        Click(ids[0], true);
        Expect(ids[..3], "起点より前へのShiftクリックも選択できる");
        Click(ids[1], release: false);
        Expect(ids[..3], "選択内の押下ではドラッグ開始に備えて選択を保持する");
        var row = pages.ContainerFromIndex(pages.Items.Cast<WorkspaceItem>().ToList().FindIndex(p => p.Id == ids[1]))!;
        window.MouseUp(row.TranslatePoint(new Point(15, 15), window)!.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Expect([ids[1]], "ドラッグせず離すと単一選択へ戻る");
        Click(ids[3], true);
        session.Editor.Document.Text = "一括移動前の編集";
        var movedIds = ids[1..4];
        check((bool)Call("MovePagesToNote", movedIds, note)!, "選択した複数ページを移動できる");
        Expect(movedIds, "移動先でも複数選択を保持する");
        check(store.Items().Where(p => movedIds.Contains(p.Id)).All(p => p.NoteId == note) &&
            store.Items().Single(p => p.Id == ids[0]).NoteId is null,
            "選択したページだけを一括移動する");
        check((long?)Field("_activePageId") == ids[1] && session.Editor.Document.Text == "一括移動前の編集" &&
            store.Items().Single(p => p.Id == ids[1]).Body == "一括移動前の編集",
            "一括移動では起点ページと編集内容を保存・保持する");
        session.Key(PhysicalKey.ArrowUp);
        Expect([ids[2]], "通常の上下移動で範囲端の隣を単一選択する");
        var before = store.Items().Single(p => p.Id == ids[0]).NoteId;
        try { store.MovePages([ids[0], note], note); }
        catch (InvalidOperationException) { }
        check(store.Items().Single(p => p.Id == ids[0]).NoteId == before, "一括移動の失敗では途中の移動を巻き戻す");
        Click(ids[2]);
        session.Editor.CaretOffset = 2;
        Click(ids[2]);
        check(session.Editor.CaretOffset == 2, "同じ起点の単一選択では編集位置を保持する");
        for (var i = 0; i < 4; i++) session.Key(PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Expect(ids[2..4], "Shift下キーは一覧末尾で止まる");
        for (var i = 0; i < 5; i++) session.Key(PhysicalKey.ArrowUp, RawInputModifiers.Shift);
        Expect(ids[1..3], "Shift上キーは一覧先頭で止まる");
        typeof(MainWindow).GetField("_noteId", flags)!.SetValue(window, null);
        Call("ReloadLists");
        Click(ids[0], true);
        Expect([ids[0]], "起点が別ノートにある場合のShiftクリックは新たな単一選択になる");
        CheckActions(check);
    }

    private static void CheckActions(Action<bool, string> check)
    {
        using var session = new EditorSession();
        var window = session.Window;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        void Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        var store = (WorkspaceStore)Field("_store")!;
        var note = store.Create("一括移動先", true, null);
        var ids = Enumerable.Range(0, 4).Select(i => store.Create("A" + i, false, null, "本文" + i)).ToArray();
        var pages = (ListBox)Field("_pages")!;
        void Select()
        {
            Call("OpenPage", ids[1]);
            Call("ReloadLists");
            Call("SelectPageRange", pages.Items.Cast<WorkspaceItem>().ToList().FindIndex(p => p.Id == ids[3]));
            pages.Focus();
        }
        void DialogButton(string label)
        {
            window.OwnedWindows.Single().GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }
        bool AllIn(long? target) => store.Items().Where(p => ids[1..].Contains(p.Id)).All(p => p.NoteId == target);
        Select();
        session.Key(PhysicalKey.M, RawInputModifiers.Control);
        DialogButton("キャンセル");
        check(AllIn(null) && pages.SelectedItems!.Count == 3, "複数ページの移動キャンセルは所属と選択を保持する");
        session.Click("ページを移動");
        window.OwnedWindows.Single().GetVisualDescendants().OfType<ComboBox>().Single().SelectedIndex = 1;
        DialogButton("移動");
        check(AllIn(note) && pages.SelectedItems!.Count == 3 && (long?)Field("_activePageId") == ids[1],
            "移動ボタンは選択全件を移動し起点と複数選択を保持する");
        session.Key(PhysicalKey.M, RawInputModifiers.Control);
        DialogButton("移動");
        check(AllIn(null) && pages.SelectedItems!.Count == 3, "Ctrl+Mでも選択全件を未分類へ移動する");
        session.Editor.Document.Text = "削除前に保存する本文";
        session.Click("ページを削除");
        check(ids[1..].All(id => store.TrashItems().Any(p => p.Id == id)) && store.Items().Any(p => p.Id == ids[0]),
            "削除ボタンは選択全件だけをごみ箱へ移す");
        check(Field("_activePageId") is null && !session.Editor.IsEnabled && pages.SelectedItems!.Count == 0 &&
            store.TrashItems().Single(p => p.Id == ids[1]).Body == "削除前に保存する本文",
            "複数削除では編集中の内容を保存し、削除した起点のエディタと選択を解除する");
        foreach (var id in ids[1..]) store.Restore(id);
        Select();
        session.Key(PhysicalKey.Delete);
        check(ids[1..].All(id => store.TrashItems().Any(p => p.Id == id)) && store.Items().Any(p => p.Id == ids[0]),
            "ページ一覧のDeleteキーでも選択全件だけをごみ箱へ移す");
    }
}

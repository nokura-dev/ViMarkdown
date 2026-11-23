using Avalonia.Controls;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private List<(string Label, Func<Task> Run)> BuildWorkspaceActions(bool notes, StackPanel actions)
    {
        var menuActions = new List<(string Label, Func<Task> Run)>();
        void Action(string label, Func<Control, Task> action)
        {
            var icon = label switch { "作成" => notes ? "note" : "page", "名前変更" => "rename", "移動" => "move", _ => "delete" };
            var name = (notes ? "ノートを" : "ページを") + label;
            var button = ToolbarButtons.Create(icon, name, name);
            async Task Run()
            {
                try { if (!OwnedWindows.Any(w => w.IsVisible)) await action(button); }
                catch (Exception ex) { _saveStatus.Text = "操作失敗: " + ex.Message; }
            }
            _workspaceActions[(notes, label)] = Run;
            button.Click += async (_, _) => await Run();
            var menuLabel = label == "作成" ? (notes ? "ノート作成" : "ページ作成")
                : label == "移動" ? "ノート移動" : label;
            menuActions.Add((menuLabel, Run));
            actions.Children.Add(button);
        }
        Action("作成", async anchor =>
        {
            if (notes)
            {
                var name = await AskName(anchor, "ノートを作成", "", "作成");
                if (name is null || !SaveCurrent()) return;
                OpenPage(null);
                _noteId = _store.Create(name, true, null);
                ReloadLists(openTimelinePage: false);
            }
            else
            {
                if (!SaveCurrent()) return;
                var name = DateTime.Now.ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                var body = name + "\n";
                var id = _store.Create(name, false, _noteId, body);
                _timelinePeriod = 0;
                _updateTimelineSelection?.Invoke();
                OpenPage(id);
                _automaticPageNameId = id;
                ReloadLists();
                _caret = _editor.CaretOffset = body.Length;
                SwitchMode(EditorMode.Insert);
                _editor.Select(body.Length, 0);
                UpdateStatus();
            }
        });
        long? Selected() => notes ? _noteId : (_pages.SelectedItem as WorkspaceItem)?.Id;
        Action("名前変更", async anchor =>
        {
            if (Selected() is not { } id) return;
            var name = await AskName(anchor, "名前変更", _store.Items().First(i => i.Id == id).Name);
            if (name is null) return; _store.Rename(id, name);
            if (_automaticPageNameId == id) _automaticPageNameId = null;
            if (_activePageId == id) Title = FormatPageWindowTitle(name);
            ReloadLists();
        });
        if (!notes) Action("移動", async anchor =>
        {
            var ids = SelectedPageIds();
            if (ids.Length == 0) return;
            var picker = new ComboBox { ItemsSource = NoteChoices(), SelectedIndex = 0, MinWidth = 250 };
            if (!await Confirm(anchor, "移動先を選択", picker, "移動")) return;
            MovePagesToNote(ids, ((NoteChoice)picker.SelectedItem!).Id);
        });
        Action("削除", anchor =>
        {
            var ids = notes ? (_noteId is { } id ? new[] { id } : Array.Empty<long>()) : SelectedPageIds();
            if (ids.Length == 0 || !SaveCurrent()) return Task.CompletedTask;
            _store.DeleteItems(ids);
            var remaining = _store.Items();
            if (_activePageId.HasValue && !remaining.Any(i => i.Id == _activePageId)) OpenPage(null);
            if (_noteId.HasValue && !remaining.Any(i => i.Id == _noteId)) _noteId = null;
            ReloadLists();
            return Task.CompletedTask;
        });
        return menuActions;
    }
}

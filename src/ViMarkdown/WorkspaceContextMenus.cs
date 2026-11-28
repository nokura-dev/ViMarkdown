using Avalonia.Controls;
using Avalonia.VisualTree;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private void AddWorkspaceContextMenu(ListBox list, bool notes,
        List<(string Label, Func<System.Threading.Tasks.Task> Run)> actions)
    {
        object? target = null;
        var commands = actions.Select(action =>
        {
            var icon = action.Label switch
            {
                "ノート作成" => "note", "ページ作成" => "page", "ページ作成(mdファイルから)" => "page",
                "名前変更" => "rename", "ノート移動" => "move", "削除" => "delete",
                _ => throw new InvalidOperationException("未定義のメニュー: " + action.Label)
            };
            var item = new MenuItem
            {
                Header = action.Label,
                Icon = ToolbarButtons.CreateIcon(icon, icon == "delete" ? WorkspaceColors.Diagnostic : WorkspaceColors.Muted)
            };
            item.Click += async (_, _) => await action.Run();
            return item;
        }).ToList();
        var openWindow = new MenuItem { Header = "別ウィンドウで開く", Icon = ToolbarButtons.CreateIcon("windows") };
        openWindow.Click += (_, _) =>
        {
            if (target is WorkspaceItem { IsNote: false } page) OpenDetachedEditor(page.Id);
        };
        var md = new MenuItem { Header = "md出力", Icon = ToolbarButtons.CreateIcon("export-md", WorkspaceColors.Accent) };
        var docx = new MenuItem { Header = "docx出力", Icon = ToolbarButtons.CreateIcon("export-docx", WorkspaceColors.Accent) };
        var xlsx = new MenuItem { Header = "xlsx出力", Icon = ToolbarButtons.CreateIcon("export-docx", WorkspaceColors.Accent) };
        list.ContextRequested += (sender, e) =>
        {
            var pointerRequest = e.TryGetPosition(list, out var position);
            // Selecting a row can rebuild the list before ContextRequested is
            // raised. Its original source may then belong to a detached row.
            // Hit-test the current list instead of using that stale source.
            if (pointerRequest) list.UpdateLayout();
            var source = pointerRequest ? list.GetVisualAt(position) : e.Source as Avalonia.Visual;
            var container = source?.GetSelfAndVisualAncestors().OfType<ListBoxItem>()
                .FirstOrDefault(row => list.IndexFromContainer(row) >= 0);
            target = container?.Content ?? (pointerRequest ? null : list.SelectedItem);
            var valid = notes ? target is NoteChoice : target is WorkspaceItem { IsNote: false };
            // Use the same selected-item actions as the toolbar. Save before
            // changing selection so a failed save cannot act on another row.
            if (valid && !Equals(list.SelectedItem, target))
            {
                if (SaveCurrent())
                {
                    list.SelectedItem = target;
                    valid = notes ? target is NoteChoice note && _noteId == note.Id
                        : target is WorkspaceItem page && _activePageId == page.Id;
                }
                else valid = false;
            }
            for (var i = 0; i < commands.Count; i++)
                commands[i].IsEnabled = (i == 0 || Equals(commands[i].Header, "ページ作成(mdファイルから)")) || (valid && (!notes || target is NoteChoice { Id: not null }));
            openWindow.IsEnabled = !notes && valid;
            md.IsEnabled = docx.IsEnabled = xlsx.IsEnabled = valid && (target switch
            {
                NoteChoice note when notes => CanExportPages(false, note.Id),
                WorkspaceItem page when !notes => CanExportPages(true, page.Id),
                _ => false
            });
        };
        var menuItems = new List<Control>();
        if (!notes)
        {
            menuItems.Add(openWindow);
            menuItems.Add(new Separator());
        }
        foreach (var command in commands)
        {
            if (Equals(command.Header, "削除")) menuItems.Add(new Separator());
            menuItems.Add(command);
        }
        menuItems.Add(new Separator());
        menuItems.Add(md);
        menuItems.Add(docx);
        menuItems.Add(xlsx);
        foreach (var item in menuItems.OfType<MenuItem>())
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.Property == MenuItem.IsEnabledProperty && item.Icon is Avalonia.Visual icon)
                    icon.Opacity = item.IsEnabled ? 1 : 0.35;
            };
        }
        list.ContextMenu = new ContextMenu { ItemsSource = menuItems };
        md.Click += async (_, _) => await ExportTargetAsync(false);
        docx.Click += async (_, _) => await ExportTargetAsync(true);

        xlsx.Click += async (_, _) => await ExportTargetAsync(false, true);

        System.Threading.Tasks.Task ExportTargetAsync(bool asDocx, bool asXlsx = false) => target switch
        {
            NoteChoice note when notes => ExportPagesAsync(asDocx, false, note.Id, asXlsx),
            WorkspaceItem page when !notes => ExportPagesAsync(asDocx, true, page.Id, asXlsx),
            _ => System.Threading.Tasks.Task.CompletedTask
        };
    }
}

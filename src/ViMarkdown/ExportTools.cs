using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private async System.Threading.Tasks.Task ExportPagesAsync(bool docx, bool singlePage, long? targetId)
    {
        try
        {
            if (!CanExportPages(singlePage, targetId) || !SaveCurrent()) return;
            var pages = _store.Items().Where(i => !i.IsNote &&
                (singlePage ? i.Id == targetId : i.NoteId == targetId)).ToList();
            if (pages.Count == 0) { _saveStatus.Text = "出力するページがありません。"; return; }
            var destinations = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "出力先", AllowMultiple = false });
            if (destinations.Count == 0) return;
            var path = destinations[0].TryGetLocalPath() ?? throw new InvalidOperationException("ローカルフォルダを選択してください。");
            var result = docx
                ? await DocxExport.WriteAsync(path, pages, _store.GetImage)
                : await MarkdownExport.WriteAsync(path, pages, _store.GetImage);
            var warning = result.MissingImages.Count == 0 ? "" : $"（欠損画像{result.MissingImages.Count}件を代替画像で出力。missing-images.txtを確認）";
            if (result.MermaidErrors.Count > 0) warning += $"（Mermaid描画失敗{result.MermaidErrors.Count}件。コード保持、mermaid-errors.txtを確認）";
            _saveStatus.Text = $"出力完了: {result.Pages}ページ・{result.Images}画像{warning} — {result.Directory}";
        }
        catch (Exception ex) { _saveStatus.Text = "出力失敗: " + ex.Message; }
    }

    private bool CanExportPages(bool singlePage, long? targetId)
    {
        return _store.Items().Any(page => !page.IsNote && (singlePage
            ? page.Id == targetId && !string.IsNullOrWhiteSpace(
                page.Id == _activePageId ? _pageContent.Text : page.Body)
            : page.NoteId == targetId));
    }

    private void AddWorkspaceContextMenu(ListBox list, bool notes,
        List<(string Label, Func<System.Threading.Tasks.Task> Run)> actions)
    {
        object? target = null;
        var commands = actions.Select(action =>
        {
            var icon = action.Label switch
            {
                "ノート作成" => "note", "ページ作成" => "page",
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
        var md = new MenuItem { Header = "md出力", Icon = ToolbarButtons.CreateIcon("export-md", WorkspaceColors.Accent) };
        var docx = new MenuItem { Header = "docx出力", Icon = ToolbarButtons.CreateIcon("export-docx", WorkspaceColors.Accent) };
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
                commands[i].IsEnabled = i == 0 || (valid && (!notes || target is NoteChoice { Id: not null }));
            md.IsEnabled = docx.IsEnabled = valid && (target switch
            {
                NoteChoice note when notes => CanExportPages(false, note.Id),
                WorkspaceItem page when !notes => CanExportPages(true, page.Id),
                _ => false
            });
        };
        var menuItems = new List<Control>();
        foreach (var command in commands)
        {
            if (Equals(command.Header, "削除")) menuItems.Add(new Separator());
            menuItems.Add(command);
        }
        menuItems.Add(new Separator());
        menuItems.Add(md);
        menuItems.Add(docx);
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

        System.Threading.Tasks.Task ExportTargetAsync(bool asDocx) => target switch
        {
            NoteChoice note when notes => ExportPagesAsync(asDocx, false, note.Id),
            WorkspaceItem page when !notes => ExportPagesAsync(asDocx, true, page.Id),
            _ => System.Threading.Tasks.Task.CompletedTask
        };
    }

}

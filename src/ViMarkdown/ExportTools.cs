using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private async Task ExportPagesAsync(bool docx, bool singlePage, long? targetId, bool xlsx = false)
    {
        try
        {
            if (!SaveAllEditors() || !CanExportPages(singlePage, targetId)) return;
            var pages = _store.Items().Where(i => !i.IsNote &&
                (singlePage ? i.Id == targetId : i.NoteId == targetId)).ToList();
            if (pages.Count == 0) { _saveStatus.Text = "出力するページがありません。"; return; }
            var destinations = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "出力先", AllowMultiple = false });
            if (destinations.Count == 0) return;
            var path = destinations[0].TryGetLocalPath() ?? throw new InvalidOperationException("ローカルフォルダを選択してください。");
            var noteId = singlePage ? pages[0].NoteId : targetId;
            var noteName = _store.Items().FirstOrDefault(item => item.IsNote && item.Id == noteId)?.Name ?? "未分類";
            var result = xlsx
                ? await XlsxExport.WriteAsync(path, pages, _store.GetImage, noteName: noteName)
                : docx
                ? await DocxExport.WriteAsync(path, pages, _store.GetImage, noteName: noteName)
                : await MarkdownExport.WriteAsync(path, pages, _store.GetImage, noteName: noteName);
            var warning = result.MissingImages.Count == 0 ? "" : $"（欠損画像{result.MissingImages.Count}件を代替画像で出力）";
            if (result.D2Errors.Count > 0) warning += $"（D2描画失敗{result.D2Errors.Count}件。コード保持）";
            if (result.ErrorReports.Count > 0) warning += "（確認: " + string.Join("、", result.ErrorReports.Values) + "）";
            _saveStatus.Text = $"出力完了: {result.Pages}ページ・{result.Images}画像{warning} — {result.Directory}";
        }
        catch (Exception ex) { _saveStatus.Text = "出力失敗: " + ex.Message; }
    }

    private bool CanExportPages(bool singlePage, long? targetId)
    {
        return _store.Items().Any(page => !page.IsNote && (singlePage
            ? page.Id == targetId && !string.IsNullOrWhiteSpace(
                ExportPageBody(page))
            : page.NoteId == targetId));
    }

    private string ExportPageBody(WorkspaceItem page)
    {
        var workspace = _workspaceOwner ?? this;
        if (workspace._detachedEditors.TryGetValue(page.Id, out var editor)) return editor._pageContent.Text;
        return page.Id == workspace._activePageId ? workspace._pageContent.Text : page.Body;
    }

}

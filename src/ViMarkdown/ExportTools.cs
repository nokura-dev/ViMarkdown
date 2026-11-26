using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private async Task ExportPagesAsync(bool docx, bool singlePage, long? targetId)
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
            if (result.D2Errors.Count > 0) warning += $"（D2描画失敗{result.D2Errors.Count}件。コード保持、d2-errors.txtを確認）";
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
}

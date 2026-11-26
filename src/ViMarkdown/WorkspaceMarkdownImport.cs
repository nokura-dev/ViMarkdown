using Avalonia.Platform.Storage;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private async Task ImportMarkdownPageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "ページ作成(mdファイルから)",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Markdown") { Patterns = ["*.md", "*.markdown"] }]
        });
        if (files.Count == 0 || !SaveCurrent()) return;
        var path = files[0].TryGetLocalPath()
            ?? throw new InvalidOperationException("ローカルのmdファイルを選択してください。");
        var (id, result) = _store.ImportMarkdownPage(path, _noteId);
        _timelinePeriod = 0;
        _updateTimelineSelection?.Invoke();
        OpenPage(id);
        ReloadLists();
        _saveStatus.Text = $"読み込み完了: {Path.GetFileName(path)}・{result.Images}画像";
        if (result.MissingImages.Count > 0)
            _saveStatus.Text += "（読み込めない画像: " + string.Join("、", result.MissingImages) + "。参照を保持）";
    }
}

public sealed partial class WorkspaceStore
{
    public (long Id, MarkdownImportResult Result) ImportMarkdownPage(string path, long? noteId)
    {
        using var transaction = _connection.BeginTransaction();
        var result = MarkdownImport.Read(path, AddImage);
        var name = PageNaming.AutomaticName(result.Body);
        if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(path);
        var id = Create(name, false, noteId, result.Body);
        transaction.Commit();
        return (id, result);
    }
}

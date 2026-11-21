using Avalonia.Input;
using Avalonia.Input.Platform;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private string _register = "";

    private Task _vimClipboardWrite = Task.CompletedTask;
    private static readonly DataFormat<string> VimSelectionFormat =
        DataFormat.CreateStringApplicationFormat("ViMarkdown.VimSelectionKind");

    private async Task WriteVimClipboardAsync(Task previous, string text, int kind)
    {
        try
        {
            await previous;
            if (Clipboard is not { } clipboard)
            {
                _saveStatus.Text = "クリップボードを利用できません";
                return;
            }
            var item = DataTransferItem.CreateText(text);
            item.Set(VimSelectionFormat, kind.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var data = new DataTransfer();
            data.Add(item);
            await clipboard.SetDataAsync(data);
        }
        catch (Exception ex) { _saveStatus.Text = "クリップボードへのコピーに失敗しました: " + ex.Message; }
    }

    private async Task PasteVimClipboardAsync(bool after, int count)
    {
        var page = _activePageId;
        var source = _pageContent.Text;
        var caret = _caret;
        var anchor = _anchor;
        var generation = _modeSwitchGeneration;
        await _vimClipboardWrite;
        if (Clipboard is not { } clipboard)
        {
            _saveStatus.Text = "クリップボードを利用できません";
            return;
        }
        try
        {
            using var data = await clipboard.TryGetDataAsync();
            if (data is null) return;
            var text = await data.TryGetTextAsync();
            if (string.IsNullOrEmpty(text)) return;
            var metadata = await data.TryGetValueAsync(VimSelectionFormat);
            var kind = metadata switch
            {
                "0" => 0,
                "1" => 1,
                "2" => 2,
                _ => text.EndsWith('\n') || text.EndsWith('\r') ? 1 : 0
            };
            // A page click, toolbar action or edit during the OS read cancels the paste.
            if (!IsVisible || page != _activePageId || source != _pageContent.Text ||
                caret != _caret || anchor != _anchor || generation != _modeSwitchGeneration) return;
            Paste(after, count, text, kind);
        }
        catch (Exception ex) { _saveStatus.Text = "クリップボードからの貼り付けに失敗しました: " + ex.Message; }
    }
}

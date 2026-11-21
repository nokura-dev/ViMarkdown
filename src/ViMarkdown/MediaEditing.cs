using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private void ConfigureMediaPaste()
    {
        AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (_commandPrompt is null && (_editor.IsKeyboardFocusWithin || _rich.IsKeyboardFocusWithin) && e.Key == Key.V &&
                (e.KeyModifiers == KeyModifiers.Meta || (_mode == EditorMode.Insert && e.KeyModifiers == KeyModifiers.Control)))
            {
                e.Handled = true;
                await PasteMedia();
            }
        }, RoutingStrategies.Tunnel);
    }

    private async Task PasteMedia()
    {
        var id = _activePageId;
        var before = _pageContent.Text;
        try
        {
            if (id is null || Clipboard is null) return;
            // Prefer encoded PNG: bitmap decoding discards its DPI metadata.
            var format = OperatingSystem.IsMacOS() ? "public.png" : OperatingSystem.IsWindows() ? "PNG" : "image/png";
            var png = await Clipboard.TryGetValueAsync(DataFormat.CreateBytesPlatformFormat(format));
            if (id != _activePageId || before != _pageContent.Text) return;
            if (png is not null && png.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            {
                InsertEncodedImage(png, ImageContent.ClipboardDpi(png, OperatingSystem.IsMacOS()));
                return;
            }
            var tiff = OperatingSystem.IsMacOS()
                ? await Clipboard.TryGetValueAsync(DataFormat.CreateBytesPlatformFormat("public.tiff")) : null;
            if (id != _activePageId || before != _pageContent.Text) return;
            using var bitmap = await Clipboard.TryGetBitmapAsync();
            if (id != _activePageId || before != _pageContent.Text) return;
            if (bitmap is not null) InsertImage(bitmap, ImageContent.ClipboardDpi(tiff, OperatingSystem.IsMacOS()));
            else
            {
                var text = await Clipboard.TryGetTextAsync();
                if (id == _activePageId && before == _pageContent.Text && text is not null) InsertMarkup(text);
            }
        }
        catch (Exception ex) { _saveStatus.Text = "貼り付け失敗: " + ex.Message; }
    }

    private void InsertImage(Bitmap bitmap, Avalonia.Vector? density = null)
    {
        if (_activePageId is null) return;
        InsertPng(ImageContent.EncodePng(bitmap, density));
    }

    private void InsertEncodedImage(byte[] data, Avalonia.Vector? density = null)
    {
        using var bitmap = new Bitmap(new MemoryStream(data));
        InsertPng(density is null && data.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            ? data : ImageContent.EncodePng(bitmap, density));
    }

    private void InsertPng(byte[] data)
    {
        if (_activePageId is null) return;
        var url = _store.AddImage(data);
        InsertMarkup($"\n\n![画像]({url})\n\n");
    }

    private void InsertMarkup(string markup)
    {
        if (_activePageId is null) return;
        CancelPendingCommand();
        if (_mode == EditorMode.Visual)
        {
            var ranges = VimSelection();
            _editor.Select(ranges[0].Start, 0);
            using (_pageContent.RunUpdate())
            {
                foreach (var range in ranges.Reverse()) _pageContent.Remove(range.Start, range.End - range.Start);
                _pageContent.Insert(ranges[0].Start, markup);
            }
            _caret = ranges[0].Start + markup.Length;
            SwitchMode(EditorMode.Normal); ClampNormal(); Refresh(); return;
        }
        var start = _mode == EditorMode.Insert ? _editor.SelectionStart : _caret;
        var length = _mode == EditorMode.Insert ? _editor.SelectionLength : 0;
        _editor.Select(start, length);
        _pageContent.Replace(start, length, markup);
        _caret = start + markup.Length;
        _anchor = null;
        _editor.Select(_caret, 0);
        Refresh();
    }
}

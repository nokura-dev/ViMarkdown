using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private const int PastedImageMaxSide = 2560;

    private async Task CopyImageAsync(byte[] data)
    {
        try
        {
            if (Clipboard is not { } clipboard)
            {
                _saveStatus.Text = "クリップボードを利用できません";
                return;
            }
            // Supply the stored PNG directly on macOS, without the native
            // bitmap conversion path. This preserves the original pixel bounds.
            IAsyncDataTransfer transfer;
            if (OperatingSystem.IsMacOS())
            {
                var pngTransfer = new DataTransfer();
                pngTransfer.Add(DataTransferItem.Create(
                    DataFormat.CreateBytesPlatformFormat("public.png"), data));
                transfer = pngTransfer;
            }
            else transfer = new ClipboardImageData(data);
            try
            {
                await clipboard.SetDataAsync(transfer);
                // Ownership passes to the clipboard; it disposes the transfer
                // when the data is no longer needed, including any bitmap.
            }
            catch
            {
                transfer.Dispose();
                throw;
            }
            _saveStatus.Text = "画像をクリップボードへコピーしました";
        }
        catch (Exception ex) { _saveStatus.Text = "画像のコピーに失敗しました: " + ex.Message; }
    }

    private void ConfigureMediaPaste()
    {
        AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (!_isSlideshow && _commandPrompt is null && (_editor.IsKeyboardFocusWithin || _rich.IsKeyboardFocusWithin) && e.Key == Key.V &&
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
        var generation = _modeSwitchGeneration;
        bool StillEditing() => IsVisible && !IsPageDetached && !_isSlideshow &&
            generation == _modeSwitchGeneration && id == _activePageId && before == _pageContent.Text;
        try
        {
            if (id is null || IsPageDetached || Clipboard is null) return;
            // Prefer encoded PNG: bitmap decoding discards its DPI metadata.
            var format = OperatingSystem.IsMacOS() ? "public.png" : OperatingSystem.IsWindows() ? "PNG" : "image/png";
            var png = await Clipboard.TryGetValueAsync(DataFormat.CreateBytesPlatformFormat(format));
            if (!StillEditing()) return;
            if (png is not null && png.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            {
                InsertEncodedImage(png, ImageContent.ClipboardDpi(png, OperatingSystem.IsMacOS()));
                return;
            }
            var tiff = OperatingSystem.IsMacOS()
                ? await Clipboard.TryGetValueAsync(DataFormat.CreateBytesPlatformFormat("public.tiff")) : null;
            if (!StillEditing()) return;
            using var bitmap = await Clipboard.TryGetBitmapAsync();
            if (!StillEditing()) return;
            if (bitmap is not null) InsertImage(bitmap, ImageContent.ClipboardDpi(tiff, OperatingSystem.IsMacOS()));
            else
            {
                var text = await Clipboard.TryGetTextAsync();
                if (StillEditing() && text is not null) InsertMarkup(text);
            }
        }
        catch (Exception ex) { _saveStatus.Text = "貼り付け失敗: " + ex.Message; }
    }

    private void InsertImage(Bitmap bitmap, Avalonia.Vector? density = null)
    {
        if (_activePageId is null) return;
        var size = bitmap.PixelSize;
        var longestSide = Math.Max(size.Width, size.Height);
        if (longestSide > PastedImageMaxSide)
        {
            var scale = (double)PastedImageMaxSide / longestSide;
            var target = new Avalonia.PixelSize(
                Math.Max(1, (int)Math.Round(size.Width * scale)),
                Math.Max(1, (int)Math.Round(size.Height * scale)));
            using var resized = bitmap.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
            InsertPng(ImageContent.EncodePng(resized, density ?? bitmap.Dpi));
            return;
        }
        InsertPng(ImageContent.EncodePng(bitmap, density));
    }

    private void InsertEncodedImage(byte[] data, Avalonia.Vector? density = null)
    {
        using var bitmap = new Bitmap(new MemoryStream(data));
        if (Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height) > PastedImageMaxSide)
        {
            InsertImage(bitmap, density);
            return;
        }
        InsertPng(density is null && data.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            ? data : ImageContent.EncodePng(bitmap, density));
    }

    private void InsertPng(byte[] data)
    {
        if (_activePageId is null) return;
        var url = _store.AddImage(data);
        InsertMarkup($"![画像]({url})\n\n");
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

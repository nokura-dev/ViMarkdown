using Avalonia.Input;
using Avalonia.Media.Imaging;

namespace ViMarkdown;

// The OS may request the bitmap after SetDataAsync has returned. Its lifetime
// belongs to the clipboard transfer, not to the method that initiates copying.
public sealed class ClipboardImageData : IDataTransfer, IAsyncDataTransfer
{
    private Bitmap? _bitmap;
    private readonly DataTransferItem[] _items;

    public ClipboardImageData(byte[] png)
    {
        using var stream = new MemoryStream(png);
        _bitmap = new Bitmap(stream);
        _items = [DataTransferItem.Create(DataFormat.Bitmap, () => _bitmap)];
    }

    public IReadOnlyList<DataFormat> Formats { get; } = [DataFormat.Bitmap];
    IReadOnlyList<IDataTransferItem> IDataTransfer.Items => _items;
    IReadOnlyList<IAsyncDataTransferItem> IAsyncDataTransfer.Items => _items;

    public void Dispose()
    {
        var bitmap = _bitmap;
        _bitmap = null;
        bitmap?.Dispose();
    }
}

using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace ViMarkdown;

internal sealed class SearchHighlight(Func<IReadOnlyList<(int Start, int End)>> ranges) : DocumentColorizingTransformer
{
    protected override void ColorizeLine(DocumentLine line)
    {
        foreach (var range in ranges())
        {
            var start = Math.Max(line.Offset, range.Start);
            var end = Math.Min(line.EndOffset, range.End);
            if (start < end) ChangeLinePart(start, end, element =>
                element.TextRunProperties.SetBackgroundBrush(WorkspaceColors.SearchMatch));
        }
    }
}

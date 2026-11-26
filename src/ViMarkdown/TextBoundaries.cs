using System.Globalization;

namespace ViMarkdown;

// Source offsets are UTF-16, matching AvaloniaEdit. Grapheme boundaries prevent
// navigation from splitting surrogate pairs, combining marks or emoji sequences.
public static class TextBoundaries
{
    public static int Next(string text, int offset)
        => StringInfo.ParseCombiningCharacters(text).FirstOrDefault(x => x > offset, text.Length);
    public static int Previous(string text, int offset)
        => StringInfo.ParseCombiningCharacters(text).LastOrDefault(x => x < offset, 0);
}

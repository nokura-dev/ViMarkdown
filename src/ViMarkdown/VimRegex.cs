namespace ViMarkdown;

internal static class VimRegex
{
    // Vim's default magic treats these operators as special only when escaped.
    public static string Pattern(string pattern)
    {
        if (pattern.StartsWith(@"\v")) return pattern[2..];
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                var next = pattern[++i];
                if (next is '<' or '>') result.Append(@"\b");
                else if ("()+?|{}".Contains(next)) result.Append(next);
                else result.Append('\\').Append(next);
            }
            else { if ("()+?|{}".Contains(c)) result.Append('\\'); result.Append(c); }
        }
        return result.ToString();
    }
    public static string Replacement(string replacement)
    {
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < replacement.Length; i++)
        {
            var c = replacement[i];
            if (c == '\\' && i + 1 < replacement.Length)
            {
                c = replacement[++i];
                if (char.IsAsciiDigit(c)) result.Append('$').Append(c);
                else result.Append(c == 'r' ? '\n' : c);
            }
            else result.Append(c == '&' ? "$&" : c == '$' ? "$$" : c.ToString());
        }
        return result.ToString();
    }
}

using Avalonia.Input;

namespace ViMarkdown;

// Keep layout/native text interpretation independent of command execution and event ordering.
// JIS fallback, shifted brackets and stale native text have integration coverage in VimCommandChecks.
internal static class VimKeyMap
{
    public static string Resolve(KeyEventArgs e, string? nativeText = null)
    {
        var useNativeText = UsesNativeText(e.Key, nativeText);
        return useNativeText ? nativeText ?? KeyText(e) : KeyText(e);
    }

    private static string KeyText(KeyEventArgs e)
    {
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        // KeySymbol reflects the active keyboard layout (for example, unshifted ^ on JIS).
        // Keep the key-code fallback for platforms that do not supply printable text.
        if (!string.IsNullOrEmpty(e.KeySymbol) && !e.KeySymbol.Any(char.IsControl) &&
            !e.KeySymbol.All(char.IsAsciiLetterOrDigit))
        {
            // Some platforms supply the unshifted bracket symbol. Resolve Shift
            // from the symbol, since JIS and US layouts use different key codes.
            return shift ? e.KeySymbol switch { "[" => "{", "]" => "}", _ => e.KeySymbol } : e.KeySymbol;
        }
        if (e.Key >= Key.A && e.Key <= Key.Z)
        {
            var c = (char)('a' + (int)e.Key - (int)Key.A);
            return (shift ? char.ToUpperInvariant(c) : c).ToString();
        }
        if (e.Key >= Key.D0 && e.Key <= Key.D9)
        {
            var n = (int)e.Key - (int)Key.D0;
            return (shift ? ")!@#$%^&*("[n] : (char)('0' + n)).ToString();
        }
        return e.Key switch
        {
            Key.Space => " ",
            Key.OemMinus => shift ? "_" : "-",
            Key.OemTilde => shift ? "~" : "`",
            Key.OemPipe => shift ? "|" : "\\",
            Key.OemSemicolon => shift ? ":" : ";",
            Key.OemQuestion => shift ? "?" : "/",
            Key.OemOpenBrackets => shift ? "{" : "[",
            Key.OemCloseBrackets => shift ? "}" : "]",
            Key.OemQuotes => shift ? "\"" : "'",
            Key.OemComma => shift ? "<" : ",",
            Key.OemPeriod => shift ? ">" : ".",
            Key.OemPlus => shift ? "+" : "=",
            Key.Left => "h",
            Key.Right => "l",
            Key.Up => "k",
            Key.Down => "j",
            Key.Home => "0",
            Key.End => "$",
            _ => ""
        };
    }
    public static bool IsLayoutPunctuation(Key key) => key is Key.OemPlus or Key.OemMinus
        or Key.OemTilde or Key.OemOpenBrackets or Key.OemCloseBrackets or Key.OemPipe
        or Key.OemBackslash or Key.OemSemicolon or Key.OemQuotes or Key.OemComma
        or Key.OemPeriod or Key.OemQuestion or Key.Oem8;

    public static bool UsesNativeText(Key key, string? text) =>
        IsLayoutPunctuation(key) && (key == Key.OemPlus || text is "^" or "{" or "}");

}

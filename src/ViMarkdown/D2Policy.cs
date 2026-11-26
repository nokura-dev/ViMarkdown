using System.Security.Cryptography;
using System.Text;

namespace ViMarkdown;

public sealed record D2Image(string Name, byte[] Bytes);
public sealed record D2Input(string Code, IReadOnlyList<D2Image> Images, string Key);

// Conservative lexical policy for the standalone subset. D2 itself validates the
// complete grammar afterwards. Never run fmt/validate before this policy: those
// commands can resolve imports too. References are replaced with owned snapshots.
public static class D2Policy
{
    private sealed record Token(string Text, int Start, int End, int Line, bool Quoted = false, bool Block = false);

    public static D2Input Prepare(string code)
    {
        if (code.Length > 100_000) throw new FormatException("コードは100,000文字以内にしてください。");
        var tokens = Lex(code);
        var edits = new List<(int Start, int End, string Value)>();
        var images = new List<D2Image>();
        var keyTokens = new List<Token>();
        var inValue = false;
        foreach (var (token, index) in tokens.Select((t, i) => (t, i)))
        {
            if (token.Block)
            {
                if (!inValue) Fail(token, "ブロック文字列によるキーの指定には対応していません。");
                // Markdown HTML/images can load files without an icon property.
                if (token.Text.Contains("![", StringComparison.Ordinal) || token.Text.Contains('<'))
                    Fail(token, "ブロックラベル内の画像・HTMLには対応していません。");
                continue;
            }
            if (!token.Quoted && token.Text is "{" or "}" or ";" or "\n")
            { keyTokens.Clear(); inValue = false; continue; }
            if (!token.Quoted && token.Text == ":" && !inValue)
            {
                foreach (var key in keyTokens)
                    if (key.Text is "layers" or "scenarios" or "steps")
                        Fail(key, "Layers・Scenarios・Stepsには対応していません。図ごとにd2フェンスを分けてください。");
                if (keyTokens.LastOrDefault()?.Text == "icon")
                {
                    if (index + 1 >= tokens.Count || !tokens[index + 1].Quoted)
                        Fail(token, "iconには二重引用符で囲んだローカル画像の絶対パスを指定してください。");
                    var value = tokens[index + 1];
                    if (index + 2 < tokens.Count && tokens[index + 2].Text is not ("\n" or ";" or "}"))
                        Fail(value, "iconは画像の絶対パス1つだけを指定してください。");
                    var name = "image-" + images.Count + ".png";
                    images.Add(new(name, ReadImage(value.Text, value.Line)));
                    if (images.Count > 32 || images.Sum(i => (long)i.Bytes.Length) > 32 * 1024 * 1024)
                        Fail(value, "画像は32件・合計32MB以内にしてください。");
                    edits.Add((value.Start, value.End, "\"" + name + "\""));
                }
                keyTokens.Clear(); inValue = true; continue;
            }
            if (!inValue && !token.Block)
            {
                // A bare board declaration (including empty boards) is unsupported.
                if (token.Text is "layers" or "scenarios" or "steps")
                    if (index + 1 == tokens.Count || tokens[index + 1].Text is ":" or "{" or ";" or "\n" or "}")
                        Fail(token, "Layers・Scenarios・Stepsには対応していません。図ごとにd2フェンスを分けてください。");
                if (token.Text != ".") keyTokens.Add(token);
            }
        }
        var rewritten = new StringBuilder(code);
        foreach (var edit in edits.OrderByDescending(e => e.Start))
            rewritten.Remove(edit.Start, edit.End - edit.Start).Insert(edit.Start, edit.Value);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(code));
        foreach (var image in images) hash.AppendData(SHA256.HashData(image.Bytes));
        return new(rewritten.ToString(), images, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static void Fail(Token token, string message) => throw new FormatException($"{message}（{token.Line}行目）");

    private static byte[] ReadImage(string path, int line)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) || path.Contains("://", StringComparison.Ordinal) ||
            extension is not (".png" or ".jpg" or ".jpeg"))
            throw new FormatException($"画像はローカルPNG/JPEGの絶対パスのみ対応しています。URL・相対パス・SVG・UNCパスは使用できません。（{line}行目）");
        // Resolve links before copying; device paths and named streams are excluded.
        path = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows() && path[2..].Contains(':'))
            throw new FormatException("画像のデバイスパス・代替データストリームは使用できません。");
        var info = new FileInfo(path);
        var target = info.ResolveLinkTarget(true);
        if (target is not null) path = target.FullName;
        if (path.StartsWith("//", StringComparison.Ordinal) || path.StartsWith("\\\\", StringComparison.Ordinal) ||
            (OperatingSystem.IsWindows() && new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network))
            throw new FormatException("ネットワーク共有の画像は使用できません。");
        using var file = File.OpenRead(path);
        if (file.Length > 8 * 1024 * 1024) throw new FormatException("画像は1件8MB以内にしてください: " + path);
        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        var png = bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var jpeg = bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
        if ((extension == ".png" && !png) || (extension is ".jpg" or ".jpeg" && !jpeg))
            throw new FormatException("拡張子と画像形式が一致しません: " + path);
        return bytes;
    }

    private static List<Token> Lex(string text)
    {
        var result = new List<Token>();
        var i = 0; var line = 1;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\r' || (char.IsWhiteSpace(c) && c != '\n')) { i++; continue; }
            var start = i; var firstLine = line;
            if (c == '#')
            {
                if (text.AsSpan(i).StartsWith("\"\"\"")) throw new FormatException("不正なコメントです。");
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (text.AsSpan(i).StartsWith("\"\"\""))
            {
                i += 3; var end = text.IndexOf("\"\"\"", i, StringComparison.Ordinal);
                if (end < 0) throw new FormatException("ブロックコメントが閉じていません。");
                line += text[i..end].Count(x => x == '\n'); i = end + 3; continue;
            }
            if (c == '|')
            {
                while (i < text.Length && text[i] == '|') i++;
                var delimiter = text[start..i]; var end = text.IndexOf(delimiter, i, StringComparison.Ordinal);
                if (end < 0) throw new FormatException($"ブロックラベルが閉じていません。（{line}行目）");
                var body = text[i..end];
                result.Add(new(body, start, end + delimiter.Length, firstLine, Block: true));
                line += body.Count(x => x == '\n'); i = end + delimiter.Length; continue;
            }
            if (c is '"' or '\'')
            {
                var quote = c; i++; var value = new StringBuilder(); var closed = false;
                while (i < text.Length)
                {
                    c = text[i++];
                    if (c == quote) { closed = true; break; }
                    if (c == '\n') line++;
                    if (c == '\\')
                    {
                        if (i == text.Length) break;
                        c = text[i++];
                        c = c switch { 'n' => '\n', 'r' => '\r', 't' => '\t', '\\' => '\\', '"' => '"', '\'' => '\'', _ => throw new FormatException("引用符内のエスケープは \\\\, \\\", \\n, \\r, \\t のみ対応しています。") };
                    }
                    value.Append(c);
                }
                if (!closed) throw new FormatException($"引用符が閉じていません。（{firstLine}行目）");
                if (quote == '"' && value.ToString().Contains("${", StringComparison.Ordinal))
                    throw new FormatException("初版では変数の展開には対応していません。");
                result.Add(new(value.ToString(), start, i, firstLine, Quoted: true)); continue;
            }
            if (c is '@' or '$' or '\\')
                throw new FormatException($"import・変数展開・引用符外のエスケープには対応していません。（{line}行目）");
            if (c == '\n') line++;
            if (c is ':' or '{' or '}' or ';' or '\n' or '.') i++;
            else
            {
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && !":{};.\"'|#@$\\".Contains(text[i])) i++;
                if (i == start) i++;
            }
            result.Add(new(text[start..i], start, i, firstLine));
        }
        return result;
    }
}

using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using ViMarkdown;

internal static class VimCommandChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var session = new EditorSession();
        var window = session.Window;
        var editor = session.Editor;
        var rich = session.Rich;
        var clipboard = window.Clipboard!;
        void Symbol(Key key, string symbol, KeyModifiers modifiers = KeyModifiers.None)
        {
            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
                Key = key, KeySymbol = symbol, KeyModifiers = modifiers
            });
            Dispatcher.UIThread.RunJobs();
        }
        // macOS/JIS can report the unshifted symbol alongside Shift, with key codes
        // different from the US bracket positions.
        foreach (var (previousKey, nextKey, previousSymbol, nextSymbol) in new[]
        {
            (Key.OemCloseBrackets, Key.OemPipe, "[", "]"),
            (Key.OemOpenBrackets, Key.OemCloseBrackets, "[", "]"),
            (Key.OemCloseBrackets, Key.OemPipe, "{", "}")
        })
        {
            session.Reset("aaa\n\nbbb\n\nccc", 5);
            Symbol(previousKey, previousSymbol, KeyModifiers.Shift);
            check(rich.Caret == 4, "Shift付きの開き括弧は前の段落境界へ移動");
            Symbol(nextKey, nextSymbol, KeyModifiers.Shift);
            check(rich.Caret == 9, "Shift付きの閉じ括弧は次の段落境界へ移動");
            check(editor.Text == "aaa\n\nbbb\n\nccc", "段落移動で本文を変更しない");
        }
        foreach (var nativeKey in new[] { Key.OemPipe, Key.OemBackslash, Key.OemCloseBrackets })
        foreach (var symbol in new string?[] { null, "\\", "|" })
        {
            session.Reset("aaa\n\nbbb\n\nccc", 5);
            void NativeBrace(string text) => typeof(MainWindow).GetMethod("HandleVimKey",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(window, new object?[] { new KeyEventArgs
                { Key = nativeKey, KeySymbol = symbol, KeyModifiers = KeyModifiers.Shift }, text });
            NativeBrace("}"); Dispatcher.UIThread.RunJobs();
            check(rich.Caret == 9, "JISの閉じ波括弧はバックスラッシュ系コードでも次の段落境界へ移動");
            NativeBrace("{"); Dispatcher.UIThread.RunJobs();
            check(rich.Caret == 4, "開き波括弧も前の段落境界へ移動");
            check(editor.Text == "aaa\n\nbbb\n\nccc", "波括弧の移動で本文を変更しない");
        }
        session.Reset("  abc", 4); session.Keys("0");
        var lineStart = rich.Caret;
        // Replay Cocoa text independently of Avalonia's missing/wrong symbol;
        // no TextInput event is sent, as RichSurface has no IME client.
        foreach (var nativeKey in new[] { Key.OemPlus, Key.OemTilde, Key.OemCloseBrackets, Key.Oem8 })
        foreach (var symbol in new string?[] { null, "=" })
        {
            session.Reset("  abc", 4);
            typeof(MainWindow).GetMethod("HandleVimKey", System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)!.Invoke(window, new object?[]
                { new KeyEventArgs { Key = nativeKey, KeySymbol = symbol }, "^" });
            Dispatcher.UIThread.RunJobs();
            check(rich.Caret == lineStart && editor.Text == "  abc", "Cocoaの^でTextInputなしでも1回で行頭へ移動");
            session.Keys("l");
            check(editor.Text == "  abc", "Cocoaの^は字下げ操作を開始しない");
        }
        foreach (var symbol in new string?[] { null, "=" })
        {
            session.Reset("  abc", 4);
            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent, Key = Key.OemPlus, KeySymbol = symbol
            });
            window.KeyTextInput("^");
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.OemPlus });
            Dispatcher.UIThread.RunJobs();
            check(rich.Caret == lineStart && editor.Text == "  abc", "JISの曖昧なキーコードでも確定文字^で1回で行頭へ移動");
            session.Keys("l");
            check(editor.Text == "  abc", "^の後に字下げオペレータを残さない");
        }
        session.Reset("  abc", 4);
        Symbol(Key.OemPlus, "^");
        check(rich.Caret == lineStart, "JIS配列の^は1回で0と同じ位置へ移動");
        Symbol(Key.OemPlus, "^");
        check(rich.Caret == lineStart, "^を繰り返しても行頭位置を維持");
        Symbol(Key.D4, "$", KeyModifiers.Shift);
        check(rich.Caret == 5, "$は最終文字の後ろへ移動");
        session.Keys("h");
        check(rich.Caret == 4, "$の後のhは最終文字へ移動");
        session.Keys("x");
        check(editor.Text == "  ab", "$の直後のxは最終文字を削除");
        session.Reset("abcdef"); session.Keys("$");
        typeof(MainWindow).GetMethod("HandleVimKey", System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance)!.Invoke(window, new object?[]
            { new KeyEventArgs { Key = Key.H, KeySymbol = "h" }, "^" });
        Dispatcher.UIThread.RunJobs();
        check(rich.Caret == 5, "macOS側に別の入力文字が残っていても$の後のhは1文字だけ戻る");
        foreach (var (text, start, last) in new[]
        {
            ("abcdef", 0, 5), ("abcdef\nnext", 0, 5),
            ("**abcdef**", 2, 7), ("**abcdef**\nnext", 2, 7),
            ("[abcdef](url)", 1, 6), ("# abcdef", 2, 7),
            ("- abcdef", 2, 7), ("```\nabcdef\n```", 4, 9),
            ("abcdef  \nnext", 0, 5),
            ("ノーマル／ビジュアルモードで表示し、挿入モードで原文を編集してください。", 0,
                "ノーマル／ビジュアルモードで表示し、挿入モードで原文を編集してください。".Length - 1)
        })
        {
            session.Reset(text, start); session.Keys("$h");
            check(rich.Caret == last, $"$hは行末の表示文字へ1文字戻る [{text}, actual={rich.Caret}]");
            var expectedBounds = rich.CaretBounds;
            session.Keys("0");
            check(rich.CaretBounds == expectedBounds || rich.CaretBounds.X < expectedBounds.X ||
                rich.CaretBounds.Y < expectedBounds.Y, "行末から戻ったカーソルは画面上でも行頭より後ろにある");
        }
        foreach (var (text, expected) in new[] { ("日本語の文章です。", 8), ("前👩‍💻", 1),
            ("前é", 1), ("前**末尾**", 4), ("前[末尾](url)", 3) })
        {
            session.Reset(text); session.Keys("$");
            session.Keys("h");
            check(rich.Caret == expected && rich.Caret > 0, "$の後のhは末尾の書記素へ戻る");
            session.Keys("h");
            check(rich.Caret < expected, "続くhも1文字ずつ左へ移動");
        }
        var sample = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "markdown-features-sample.md"));
        var sampleStart = sample.IndexOf("ノーマル／ビジュアルモード", StringComparison.Ordinal);
        session.Reset(sample, sampleStart); session.Keys("$h");
        var sampleEnd = editor.Document.GetLineByOffset(sampleStart).EndOffset;
        check(rich.Caret == sampleEnd - 1, "実際のMarkdown機能追加サンプルの4行目で$hは句点へ戻る");
        session.Keys("h");
        check(rich.Caret == sampleEnd - 2, "サンプルの続くhも1文字だけ戻る");
        session.Reset("abc\ndef"); session.Keys("2$");
        check(rich.Caret == 7, "回数指定の$は指定行の最終文字の後ろへ移動");
        session.Reset("abc\ndef", 1); session.Keys("d$");
        check(editor.Text == "a\ndef", "d$は改行を残して最終文字まで削除");
        void SetClipboard(string text) => clipboard.SetTextAsync(text).GetAwaiter().GetResult();
        string? ClipboardText() => clipboard.TryGetTextAsync().GetAwaiter().GetResult();
        session.Reset("one two"); session.Keys("yw"); check(ClipboardText() == "one ", "ywはOSクリップボードにコピー");
        session.Reset("one\ntwo"); session.Keys("yy"); check(ClipboardText() == "one\n", "yyは改行付きでOSクリップボードにコピー");
        session.Reset("abc"); SetClipboard("external"); session.Keys("p"); check(editor.Text == "aexternalbc", "pは外部からコピーした内容を貼り付け");
        session.Reset("abc"); SetClipboard("external"); session.Keys("P"); check(editor.Text == "externalabc", "Pは外部の内容をカーソル前に貼り付け");
        session.Reset("abc"); SetClipboard("line\n"); session.Keys("p"); check(editor.Text == "abc\nline", "外部の末尾改行付きテキストを行として貼り付け");
        session.Reset("abc"); SetClipboard(""); session.Keys("p"); check(editor.Text == "abc", "空クリップボードではレジスタへフォールバックしない");
        session.Reset("abc"); session.Keys("x"); check(ClipboardText() == "a", "xは削除文字をクリップボードにコピー"); session.Keys("p"); check(editor.Text == "bac", "xの削除内容をpで貼り付け");
        session.Reset("one two"); session.Keys("dw"); check(ClipboardText() == "one ", "dwは削除範囲をクリップボードにコピー");
        session.Reset("one\ntwo"); session.Keys("dd"); check(ClipboardText() == "one\n", "ddは削除行をクリップボードにコピー"); session.Keys("p"); check(editor.Text == "two\none", "ddの削除行をpで貼り付け");
        session.Reset("abcd"); session.Keys("vly"); check(ClipboardText() == "ab", "ビジュアルyは両端を含めてクリップボードへコピー");
        session.Reset("abcd\nefgh"); session.Key(PhysicalKey.V, RawInputModifiers.Control); session.Keys("jly"); check(ClipboardText() == "ab\nef", "矩形yは行ごとにクリップボードへコピー"); session.Keys("p"); check(editor.Text == "aabbcd\neeffgh", "クリップボードの矩形情報をpで維持");
        session.Reset("abc"); SetClipboard("X"); session.Keys("vld"); check(ClipboardText() == "ab", "ビジュアルdは選択内容をクリップボードへコピー");
        session.Reset("abc"); SetClipboard("X"); session.Keys("vlp"); check(editor.Text == "Xc", "ビジュアルpは選択範囲をクリップボードの内容で置換"); session.Keys("u"); check(editor.Text == "abc", "ビジュアル貼り付けを1回でUndo");
        session.Reset("a\nb", 2); SetClipboard("X\n"); session.Keys("Vp"); check(editor.Text == "a\nX", "最終行の選択をクリップボードの行で置換");
        session.Reset("a\nb\nc", 2); SetClipboard("X"); session.Keys("VP"); check(editor.Text == "a\nX\nc", "行選択へのテキスト貼り付けで前後の行を保持");
        session.Reset("abc"); SetClipboard("untouched"); session.Keys("d");
        using (var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500))) Dispatcher.UIThread.MainLoop(timeout.Token);
        check(editor.Text == "abc" && ClipboardText() == "untouched", "dは時間経過で本文やクリップボードを変えずオペレータ入力を待つ");
        session.Key(PhysicalKey.Escape); session.Keys("dl"); check(editor.Text == "bc" && rich.Caret == 0, "dlは移動先まで削除");
        foreach (var letter in new[] { "日", "A", "👩‍💻", "é" })
        {
            session.Reset("前" + letter + "後", 1); session.Keys("x"); check(editor.Text == "前後", "xで書記素を削除");
            session.Keys("u"); check(editor.Text == "前" + letter + "後" && rich.Caret == 1, "xをUndo");
            session.Key(PhysicalKey.R, RawInputModifiers.Control); check(editor.Text == "前後", "削除をRedo");
        }
        foreach (var (text, offset, expected) in new[] { ("最初\n中央\n最後", 0, "中央\n最後"), ("最初\n中央\n最後", 3, "最初\n最後"), ("最初\n中央\n最後", 6, "最初\n中央"), ("一行だけ", 0, ""), ("", 0, ""), ("前\r\n後", 3, "前") })
        {
            session.Reset(text, offset); session.Keys("dd"); check(editor.Text == expected, $"ddは原文行を改行込みで削除 [offset={offset}, actual={editor.Text.Replace("\n", "/")}, caret={rich.Caret}]");
            if (text.Length > 0) { session.Keys("u"); check(editor.Text == text && !editor.Document.UndoStack.CanUndo, "ddを1回のUndoで復元"); }
        }
        session.Reset("  abc\n  def", 9); session.Keys("yy"); check(rich.Caret == 9, "yyは元の列を維持"); session.Keys("dd"); check(editor.Text == "  abc" && rich.Caret == 2, "最終行のddは前行の最初の非空白文字へ移動");
        session.Reset("one.two three.four"); session.Keys("W"); check(rich.Caret == 8, "Wで空白区切りの次の単語"); session.Keys("E"); check(rich.Caret == 17, "Eで空白区切りの単語末尾"); session.Keys("B"); check(rich.Caret == 8, "Bで空白区切りの単語先頭");
        session.Reset("one two three\nfour five\nsix"); session.Keys("w"); check(rich.Caret == 4, "wで次の単語");
        session.Keys("e"); check(rich.Caret == 6, "eで単語末尾"); session.Keys("b"); check(rich.Caret == 4, "bで単語先頭");
        session.Keys("0"); check(rich.Caret == 0, "0で行頭"); session.Keys("$"); check(rich.Caret == 13, "$で最終文字の後ろ");
        session.Keys("2G"); check(rich.Caret == 14, "行番号Gで指定行"); session.Keys("G"); check(rich.Caret == 24, "Gで最終行"); session.Keys("gg"); check(rich.Caret == 0, "ggで先頭行");
        session.Reset("a\n\nb\n\nc"); session.Keys("}"); check(rich.Caret == 2, "}で次の段落境界"); session.Keys("{"); check(rich.Caret == 0, "{で前の段落境界");
        session.Reset("one two three\nfour five\nsix");
        session.Keys("2dw"); check(editor.Text.StartsWith("three"), "2dwで2単語削除"); session.Keys("u");
        session.Keys("d2w"); check(editor.Text.StartsWith("three"), "d2wで移動の回数指定");
        session.Reset("a\nb\nc\nd\ne\nf\ng"); session.Keys("2d3d"); check(editor.Text == "g", "オペレータと移動の回数を乗算");
        foreach (var text in new[] { "  abc", "```\n  abc\n```", "# abc", "\tabc" })
        {
            var offset = text.IndexOf("abc", StringComparison.Ordinal) + 2;
            session.Reset(text, offset); session.Keys("0"); var expected = rich.Caret;
            check(rich.MoveDisplayed(offset, "^", 1) == rich.MoveDisplayed(offset, "0", 1),
                "プレビューの直接移動でも^と0を統一");
            session.Reset(text, offset); session.Keys("^");
            check(rich.Caret == expected, "^は空白・コード・装飾のある行でも0と同じ位置へ移動");
        }
        foreach (var op in new[] { "d", "c", "y", ">", "<", "=" })
        {
            session.Reset("  abc", 4); session.Keys(op + "0");
            var expectedText = editor.Text; var expectedCaret = rich.Caret;
            session.Reset("  abc", 4); session.Keys(op + "^");
            check(editor.Text == expectedText && rich.Caret == expectedCaret, op + "^は" + op + "0と同じ動作");
        }
        session.Reset("  abc\ndef"); session.Keys("^");
        session.Keys("I"); window.KeyTextInput("X"); session.Key(PhysicalKey.Escape); check(editor.Text.StartsWith("  Xabc"), "Iで空白後に挿入");
        session.Keys("A"); window.KeyTextInput("Y"); session.Key(PhysicalKey.Escape); check(editor.Text.StartsWith("  XabcY\n"), "Aで行末挿入");
        session.Keys("o"); window.KeyTextInput("new"); session.Key(PhysicalKey.Escape); check(editor.Text.Contains("\n  new\n"), "oで次行を開き字下げ継承");
        session.Keys("O"); window.KeyTextInput("above"); session.Key(PhysicalKey.Escape); check(editor.Text.Contains("\n  above\n  new"), "Oで上に行を開く");
        session.Reset("alpha beta"); session.Keys("cw"); window.KeyTextInput("X"); session.Key(PhysicalKey.Escape); check(editor.Text == "X beta", "cwは後続空白を保持"); session.Keys("u"); check(editor.Text == "alpha beta", "変更と挿入を1回のUndoで復元");
        session.Reset("a b"); session.Keys("cw"); window.KeyTextInput("X"); session.Key(PhysicalKey.Escape); check(editor.Text == "X b", "cwで1文字の単語を変更");
        session.Reset("abc\ndef"); session.Keys("cc"); window.KeyTextInput("X"); session.Key(PhysicalKey.Escape); check(editor.Text == "X\ndef", "ccは行を置き換え");
        session.Reset("abcdef", 2); session.Keys("C"); window.KeyTextInput("X"); session.Key(PhysicalKey.Escape); check(editor.Text == "abX", "Cは行末まで変更");
        session.Reset("abc"); session.Keys("s"); window.KeyTextInput("X"); session.Key(PhysicalKey.Escape); check(editor.Text == "Xbc", "sで1文字を変更");
        session.Reset("abc\ndef"); session.Keys("S"); window.KeyTextInput("X"); session.Key(PhysicalKey.Escape); check(editor.Text == "X\ndef", "Sで行を変更");
        session.Reset("abc"); session.Keys("2rX"); check(editor.Text == "XXc" && rich.Caret == 1, "回数指定rで文字置換して最後の置換位置へ移動");
        session.Reset("abc"); session.Keys("r "); check(editor.Text == " bc", "rで空白に置換");
        session.Reset("abc"); session.Keys("4rx"); check(editor.Text == "abc", "rは行末を超える回数指定を拒否");
        session.Reset("abc"); session.Keys("R"); window.KeyTextInput("XY"); session.Key(PhysicalKey.Escape); check(editor.Text == "XYc", "Rで上書き入力");
        session.Reset("one\ntwo"); session.Keys("yyp"); check(editor.Text == "one\none\ntwo", "yyとpで行コピー");
        session.Reset("a\r\nb"); session.Keys("yyGp"); check(editor.Text == "a\r\nb\r\na", "最終行への行貼り付けでCRLFを維持");
        session.Reset("a\r\nb"); session.Keys("o"); window.KeyTextInput("X"); session.Key(PhysicalKey.Escape); check(editor.Text == "a\r\nX\r\nb", "oでCRLFを維持");
        session.Reset("one two"); session.Keys("ywp"); check(editor.Text == "oone ne two", "ywとpで文字単位コピー");
        session.Reset("abc\ndef", 4); session.Keys("yyP"); check(editor.Text == "abc\ndef\ndef", "Pで行を上に貼り付け");
        session.Reset("one two", 1); session.Keys("diw"); check(editor.Text == " two", "diwで単語内部を削除");
        session.Reset("one two", 1); session.Keys("daw"); check(editor.Text == "two", "dawで周辺空白も削除");
        session.Reset("a(b(c)d)e", 4); session.Keys("2di("); check(editor.Text == "a()e", "入れ子のテキストオブジェクト");
        session.Reset("say \"hello\"", 6); session.Keys("ci\""); window.KeyTextInput("X"); session.Key(PhysicalKey.Escape); check(editor.Text == "say \"X\"", "ci引用符で内部変更");
        session.Reset("abc\ndef"); session.Keys("v$d"); check(editor.Text == "def", "v$は行末の改行も選択");
        session.Reset("abc\ndef"); session.Keys("v3ld"); check(editor.Text == "def", "vのlで改行まで選択できる");
        session.Reset("abcd", 1); session.Keys("vd"); check(editor.Text == "acd", "v直後はカーソル文字を含めて選択");
        session.Reset("abcd", 1); session.Keys("vld"); check(editor.Text == "ad", "vの前方選択は両端を含む");
        session.Reset("abcd", 2); session.Keys("vhd"); check(editor.Text == "ad", "vの後方選択は両端を含む");
        session.Reset("A👩‍💻éZ", 1); session.Keys("vld"); check(editor.Text == "AZ", "vは書記素全体を含む");
        session.Reset("one\ntwo\nthree"); session.Keys("Vjd"); check(editor.Text == "three", "Vは行単位選択");
        session.Reset("abcd\nefgh"); session.Key(PhysicalKey.V, RawInputModifiers.Control); session.Keys("jld"); check(editor.Text == "cd\ngh", "Ctrl+vは矩形選択");
        session.Reset("abcd\nefgh"); session.Key(PhysicalKey.V, RawInputModifiers.Control); session.Keys("jlc"); window.KeyTextInput("X"); session.Key(PhysicalKey.Escape); check(editor.Text == "Xcd\nXgh", "矩形範囲を変更して全行に入力"); session.Keys("u"); check(editor.Text == "abcd\nefgh", "矩形変更と入力を1回でUndo");
        session.Reset("abcd\nefgh"); session.Key(PhysicalKey.V, RawInputModifiers.Control); session.Keys("jlI"); window.KeyTextInput("X"); session.Key(PhysicalKey.Escape); check(editor.Text == "Xabcd\nXefgh", "矩形Iで各行の選択先頭へ挿入");
        session.Reset("abcd\nefgh"); session.Key(PhysicalKey.V, RawInputModifiers.Control); session.Keys("jlA"); window.KeyTextInput("X"); session.Key(PhysicalKey.Escape); check(editor.Text == "abXcd\nefXgh", "矩形Aで各行の選択末尾へ追記");
        session.Reset("abcd"); session.Keys("vl"); session.Key(PhysicalKey.Escape); session.Keys("gvd"); check(editor.Text == "cd", "gvで前回選択を復元");
        session.Reset("one two one"); session.Prompt("/", "one"); check(rich.Caret == 8, "/で前方検索"); session.Keys("n"); check(rich.Caret == 0, "nで折り返し検索"); session.Keys("N"); check(rich.Caret == 8, "Nで逆方向検索");
        session.Prompt("?", "two"); check(rich.Caret == 4, "?で後方検索");
        session.Reset("one two one"); session.Keys("*"); check(rich.Caret == 8, "*でカーソル単語を前方検索"); session.Keys("#"); check(rich.Caret == 0, "#でカーソル単語を後方検索");
        session.Reset("a\n  b\nc"); session.Keys("3J"); check(editor.Text == "a b c", "Jで行結合"); session.Keys("u"); check(editor.Text == "a\n  b\nc", "複数行の結合を1回のUndoで復元");
        session.Reset("a\nb\nc"); session.Keys("2>>"); check(editor.Text == "    a\n    b\nc", "回数指定で字下げ"); session.Keys("<<"); check(editor.Text == "a\n    b\nc", "字下げ解除"); session.Keys("j=="); check(editor.Text == "a\nb\nc", "=で前行に字下げを揃える");
        session.Reset("red red\nred"); session.Prompt(":", "%s/red/blue/g"); check(editor.Text == "blue blue\nblue", "全行のg置換"); session.Keys("u"); check(editor.Text == "red red\nred", "全置換を1回のUndoで復元");
        session.Prompt(":", "s/red/X/"); check(editor.Text == "X red\nred", "現在行の最初だけ置換");
        session.Prompt(":", "2s/red/Y/"); check(editor.Text == "X red\nY", "行番号を指定して置換");
        session.Reset("aa ab\naa"); session.Prompt(":", @"%s/\(a\)a/[\1&]/g"); check(editor.Text == "[aaa] ab\n[aaa]", "Vim形式のキャプチャと置換の後方参照");
        session.Reset("a\nb\nc"); session.Keys("Vj"); session.Prompt(":", "s/./X/"); check(editor.Text == "X\nX\nc", "ビジュアル選択の行範囲で置換");
        session.Prompt(":", "w"); check(window.IsVisible, "wは保存してウィンドウを維持");
        session.Reset("![画像](asset://test)\n次"); session.Keys("x"); check(editor.Text == "次", "画像上のxは原文行を削除");
        session.Reset("`![画像](asset://test)`", 1); session.Keys("x"); check(editor.Text == "`[画像](asset://test)`", "コード内の画像表記は文字単位削除");
        session.Prompt(":", "wq"); check(!window.IsVisible, "wqで保存して終了");
    }
}

internal static class AffectedChecksChecks
{
    public static void Run(Action<bool, string> check, IReadOnlyList<string> suites)
    {
        var affected = CheckOptions.Parse(["--affected"]);
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/ViMarkdown/DocxExport.cs"] = "docx-1",
            ["src/ViMarkdown/MarkdownExport.cs"] = "markdown-1",
            ["src/ViMarkdown/VimKeyMap.cs"] = "keymap-1",
            ["src/ViMarkdown/WorkspaceSearch.cs"] = "search-1",
            ["src/ViMarkdown/MainWindow.cs"] = "window-1",
            ["src/ViMarkdown/ViMarkdown.csproj"] = "project-1",
            ["src/ViMarkdown/Unmapped.cs"] = "unmapped-1",
            ["tests/ViMarkdown.Checks/EditorSession.cs"] = "fixture-1",
            ["tests/ViMarkdown.Checks/DocxChecks.cs"] = "test-1",
            ["tests/ViMarkdown.Checks/PageInkAlignmentChecks.cs"] = "ink-1",
            ["docs/markdown-features-sample.md"] = "sample-1"
        };
        var original = new CheckSnapshot("environment-1", files);
        var full = AffectedChecks.Select(suites, CheckOptions.Parse(["--all"]), original, null);
        var history = AffectedChecks.RecordSuccess(suites, full, original, null);
        CheckSnapshot Changed(string path) => new(original.Context, new(files, StringComparer.Ordinal) { [path] = "changed" });
        CheckPlan Select(CheckSnapshot snapshot, CheckHistory? state = null) =>
            AffectedChecks.Select(suites, affected, snapshot, state ?? history);
        void Exact(CheckPlan plan, params string[] names) => check(plan.Reasons.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(names),
            "影響するスイートだけを選択: " + string.Join(", ", names));

        check(AffectedChecks.Select(suites, affected, original, null).Reasons.Count == suites.Count, "初回は全件実行");
        check(AffectedChecks.Select(suites, affected, original, new CheckHistory { Version = 99 }).Reasons.Count == suites.Count,
            "未知の記録形式は全件実行");
        Exact(Select(original));
        Exact(Select(Changed("src/ViMarkdown/DocxExport.cs")), nameof(DocxChecks));
        Exact(Select(Changed("src/ViMarkdown/MarkdownExport.cs")), nameof(ExportChecks), nameof(DocxChecks), nameof(D2Checks));
        Exact(Select(Changed("src/ViMarkdown/WorkspaceSearch.cs")), nameof(WorkspaceSearchChecks));
        Exact(Select(Changed("tests/ViMarkdown.Checks/DocxChecks.cs")), nameof(DocxChecks));
        Exact(Select(Changed("tests/ViMarkdown.Checks/PageInkAlignmentChecks.cs")), nameof(PageInkAlignmentChecks), nameof(ModeAlignmentChecks));
        Exact(Select(Changed("docs/markdown-features-sample.md")), nameof(ProjectionChecks), nameof(VimCommandChecks));
        foreach (var path in new[] { "src/ViMarkdown/MainWindow.cs", "src/ViMarkdown/ViMarkdown.csproj",
            "src/ViMarkdown/Unmapped.cs", "tests/ViMarkdown.Checks/EditorSession.cs" })
            check(Select(Changed(path)).Reasons.Count == suites.Count, "共通・未登録入力の変更は全件: " + path);
        var keymap = Select(Changed("src/ViMarkdown/VimKeyMap.cs"));
        foreach (var suite in new[] { nameof(VimCommandChecks), nameof(MovementAuditChecks), nameof(LineEndEditingChecks),
            nameof(ParagraphNavigationChecks), nameof(NavigationChecks), nameof(ImeChecks), nameof(FocusChecks),
            nameof(HistoryChecks), nameof(WorkspaceShortcutChecks), nameof(WorkspaceSearchChecks) })
            check(keymap.Includes(suite), "キーマップ変更の必須回帰: " + suite);
        check(!keymap.Includes(nameof(DocxChecks)), "無関係なdocxの単体検証はキーマップ変更では選ばない");
        check(keymap.Reasons.Values.All(r => r.Any(reason => reason.Contains("VimKeyMap.cs"))), "選択理由に変更ファイルを表示");

        foreach (var path in new[] { "src/ViMarkdown/NewInput.cs", "tests/ViMarkdown.Checks/NewChecks.cs" })
        {
            var added = new CheckSnapshot(original.Context, new(files, StringComparer.Ordinal) { [path] = "new" });
            check(Select(added).Reasons.Count == suites.Count, "ファイル追加は全件: " + path);
        }
        var removedFiles = new Dictionary<string, string>(files, StringComparer.Ordinal);
        removedFiles.Remove("src/ViMarkdown/DocxExport.cs");
        check(Select(new(original.Context, removedFiles)).Reasons.Count == suites.Count, "削除は対応表にあるファイルでも全件");
        removedFiles["src/ViMarkdown/RenamedExport.cs"] = "docx-1";
        check(Select(new(original.Context, removedFiles)).Reasons.Count == suites.Count, "名前変更も全件");
        check(Select(new("environment-2", files)).Reasons.Count == suites.Count, "OS・SDK・依存関係など実行条件の変更は全件");
        check(AffectedChecks.Select(suites, affected, original, history, "入力を取得できない").Reasons.Count == suites.Count,
            "入力を調べられない場合に省略しない");

        var docxChanged = Changed("src/ViMarkdown/DocxExport.cs");
        var docxPlan = Select(docxChanged);
        var updated = AffectedChecks.RecordSuccess(suites, docxPlan, docxChanged, history);
        Exact(Select(docxChanged, updated));
        Exact(Select(docxChanged), nameof(DocxChecks)); // A failed run never records the planned changes.
        check(history.Suites[nameof(DocxChecks)].Files["src/ViMarkdown/DocxExport.cs"] == "docx-1",
            "成功記録の更新で元の記録を変更しない");
        var bothChanged = new CheckSnapshot(original.Context, new(docxChanged.Files, StringComparer.Ordinal)
        { ["src/ViMarkdown/WorkspaceSearch.cs"] = "changed" });
        Exact(Select(bothChanged, updated), nameof(WorkspaceSearchChecks));
        var manual = AffectedChecks.Select(suites, CheckOptions.Parse(["--suite", "DocxChecks"]), original, null);
        var partialHistory = AffectedChecks.RecordSuccess(suites, manual, original, null);
        check(AffectedChecks.Select(suites, affected, original, partialHistory).Reasons.Count == suites.Count,
            "手動の一部成功だけでは初回の全件実行を省略しない");
        var changedEnvironment = new CheckSnapshot("environment-2", files);
        var partialEnvironment = AffectedChecks.RecordSuccess(suites, manual, changedEnvironment, history);
        check(Select(changedEnvironment, partialEnvironment).Reasons.Count == suites.Count,
            "環境変更後の一部成功では全件基準を更新しない");
        var incomplete = AffectedChecks.RecordSuccess(suites, full, original, null);
        incomplete.Suites.Remove(nameof(DocxChecks));
        Exact(Select(original, incomplete), nameof(DocxChecks));

        var overrideAll = CheckOptions.Parse(["--affected", "--suite", "unknown", "--all"]);
        check(AffectedChecks.Select(suites, overrideAll, original, history).Reasons.Count == suites.Count,
            "全件指示は自動選択・手動選択・成功済み状態より優先");
        check(AffectedChecks.Select(suites, CheckOptions.Parse([]), original, history).Reasons.Count == suites.Count,
            "引数なしは従来どおり全件");
        var repeated = CheckOptions.Parse(["--suite", "docxchecks", "--suite", "DocxChecks,ExportChecks"]);
        Exact(AffectedChecks.Select(suites, repeated, original, history), nameof(DocxChecks), nameof(ExportChecks));
        var render = CheckOptions.Parse(["--affected", "--render-workspace", "image.png", "--dry-run"]);
        Exact(AffectedChecks.Select(suites, render, original, history), nameof(EditorViewChecks));
        check(render.DryRun && render.RenderPath("--render-workspace") == "image.png", "選択と描画指定を併用できる");
        Exact(AffectedChecks.Select(suites, CheckOptions.Parse(["--affected", "--d2"]), original, history), nameof(D2Checks));
        foreach (var args in new[] { new[] { "--suite" }, ["--suite", ",,,"], ["--render"], ["--unknown"],
            ["--affected", "--suite", "DocxChecks"] })
        {
            var rejected = false;
            try { CheckOptions.Parse(args); } catch (ArgumentException) { rejected = true; }
            check(rejected, "不正な引数を拒否: " + string.Join(" ", args));
        }
        var unknownRejected = false;
        try { AffectedChecks.Select(suites, CheckOptions.Parse(["--suite", "TypoChecks"]), original, history); }
        catch (ArgumentException) { unknownRejected = true; }
        check(unknownRejected, "不明なスイートを黙って省略しない");

        var directory = Path.Combine(Path.GetTempPath(), "affected-checks-" + Guid.NewGuid().ToString("N"));
        var statePath = Path.Combine(directory, "state.json");
        Directory.CreateDirectory(directory);
        try
        {
            check(AffectedChecks.Load(statePath) is null, "成功記録がない場合は未検証として扱う");
            foreach (var text in new[] { "{broken", "null", "{}", "{\"Version\":1,\"Suites\":null}",
                "{\"Version\":1,\"Suites\":{\"DocxChecks\":{\"Context\":\"x\",\"Files\":null}}}" })
            {
                File.WriteAllText(statePath, text);
                check(AffectedChecks.Load(statePath) is null, "破損・不完全な記録は未検証として扱う");
            }
            AffectedChecks.Save(statePath, history);
            var restored = AffectedChecks.Load(statePath)!;
            Exact(Select(original, restored));
            var bytes = File.ReadAllBytes(statePath);
            AffectedChecks.Select(suites, CheckOptions.Parse(["--affected", "--dry-run"]), docxChanged, restored);
            check(File.ReadAllBytes(statePath).SequenceEqual(bytes), "選択・dry-runだけでは成功記録を書き換えない");
            check(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "成功記録の保存後に一時ファイルを残さない");
        }
        finally { Directory.Delete(directory, recursive: true); }

        check(CheckInputs.Same(original, new(original.Context, new(files, StringComparer.Ordinal))), "入力の一致を辞書順に依存せず比較");
        check(!CheckInputs.Same(original, docxChanged), "実行中のソース変更を検出");
        check(!CheckInputs.Same(original, changedEnvironment), "実行中の環境変更を検出");

        var root = CheckInputs.FindRoot();
        var live = CheckInputs.Capture(root, d2: false);
        check(CheckInputs.MatchesBuild(root, live), "実際のビルド時マニフェストが現在のソースと一致");
        var stale = new CheckSnapshot(live.Context, new(live.Files, StringComparer.Ordinal)
        { ["src/ViMarkdown/DocxExport.cs"] = "newer-than-build" });
        check(!CheckInputs.MatchesBuild(root, stale), "古いバイナリでは新しいソースを成功として記録しない");
        check(live.Files.Keys.All(path => TestImpactRules.ForFile(path, suites).All(suites.Contains)),
            "対応表は存在するスイートだけを選択する");
        CheckWatchedInputs(check);
    }

    private static void CheckWatchedInputs(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "check-inputs-" + Guid.NewGuid().ToString("N"));
        void Write(string path, string text)
        {
            var file = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
        try
        {
            Write("src/App.cs", "code");
            Write("tests/ExampleChecks.cs", "test");
            Write("scripts/package.sh", "script");
            Write(".github/workflows/check.yml", "workflow");
            Write("tools/d2/version.json", "lock");
            Write("docs/markdown-features-sample.md", "fixture");
            Write("NuGet.Config", "configuration");
            foreach (var path in new[] { "src/bin/output.cs", "tests/obj/generated.cs", "tools/d2/node_modules/module.js",
                "build/app.exe", ".cache/checks/state.json", "docs/notes.md", "README.md" }) Write(path, "ignored");
            var first = CheckInputs.Capture(root, d2: false);
            check(first.Files.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "src/App.cs", "tests/ExampleChecks.cs",
                "scripts/package.sh", ".github/workflows/check.yml", "tools/d2/version.json",
                "docs/markdown-features-sample.md", "NuGet.Config" }),
                "ソース・ビルド設定・テストデータを監視し、生成物と通常ドキュメントを除外する");
            Write("docs/notes.md", "updated");
            check(CheckInputs.Same(first, CheckInputs.Capture(root, d2: false)), "通常ドキュメント変更では再検証を要求しない");
            Write("src/new.json", "input");
            check(!CheckInputs.Same(first, CheckInputs.Capture(root, d2: false)), "未登録の入力ファイル追加も検出する");
            var realD2 = CheckInputs.Capture(root, d2: true);
            check(realD2.Context != first.Context, "通常のD2チェック成功を実エンジンの成功と混同しない");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}

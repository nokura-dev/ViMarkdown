using Avalonia;
using Avalonia.Headless;
using ViMarkdown;

try { RunChecks(args); }
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL " + ex);
    Environment.ExitCode = 1;
}

static void RunChecks(string[] args)
{
    var options = CheckOptions.Parse(args);
    var checks = 0;
    var suiteName = "";
    var suites = new List<(string Name, Action Body)>();
    void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"{suiteName}: {name}");
        checks++;
    }
    void Run(string name, Action suite) => suites.Add((name, suite));
    string? RenderPath(string option) => options.RenderPath(option);

    Run(nameof(ProjectionChecks), () => ProjectionChecks.Run(Check));
    Run(nameof(LayoutChecks), () => LayoutChecks.Run(Check, RenderPath("--render")));
    Run(nameof(MarkdownSyntaxChecks), () => MarkdownSyntaxChecks.Run(Check));
    Run(nameof(NavigationChecks), () => NavigationChecks.Run(Check));
    Run(nameof(PageInkAlignmentChecks), () => PageInkAlignmentChecks.Run(Check));
    Run(nameof(ModeAlignmentChecks), () => ModeAlignmentChecks.Run(Check));
    Run(nameof(InsertLineEmphasisChecks), () => InsertLineEmphasisChecks.Run(Check));
    Run(nameof(MarkdownHighlightChecks), () => MarkdownHighlightChecks.Run(Check, RenderPath("--render-highlight")));
    Run(nameof(EditorViewChecks), () => EditorViewChecks.Run(Check, RenderPath("--render-insert"), RenderPath("--render-workspace")));
    Run(nameof(PaneNavigationChecks), () => PaneNavigationChecks.Run(Check));
    Run(nameof(FocusChecks), () => FocusChecks.Run(Check));
    Run(nameof(ImeChecks), () => ImeChecks.Run(Check));
    Run(nameof(MediaChecks), () => MediaChecks.Run(Check));
    Run(nameof(ImageSizingChecks), () => ImageSizingChecks.Run(Check));
    Run(nameof(ExportChecks), () => ExportChecks.Run(Check));
    Run(nameof(DocxChecks), () => DocxChecks.Run(Check));
    Run(nameof(TrashChecks), () => TrashChecks.Run(Check));
    Run(nameof(WorkspaceChecks), () => WorkspaceChecks.Run(Check));
    Run(nameof(WorkspaceSearchChecks), () => WorkspaceSearchChecks.Run(Check, RenderPath("--render-search")));
    Run(nameof(WorkspaceShortcutChecks), () => WorkspaceShortcutChecks.Run(Check));
    Run(nameof(WorkspaceMenuChecks), () => WorkspaceMenuChecks.Run(Check));
    Run(nameof(PageMoveChecks), () => PageMoveChecks.Run(Check));
    Run(nameof(WindowPlacementChecks), () => WindowPlacementChecks.Run(Check));
    Run(nameof(WorkspacePaneChecks), () => WorkspacePaneChecks.Run(Check));
    Run(nameof(HistoryChecks), () => HistoryChecks.Run(Check));
    Run(nameof(VimCommandChecks), () => VimCommandChecks.Run(Check));
    Run(nameof(MovementAuditChecks), () => MovementAuditChecks.Run(Check));
    Run(nameof(LineEndEditingChecks), () => LineEndEditingChecks.Run(Check));
    Run(nameof(ParagraphNavigationChecks), () => ParagraphNavigationChecks.Run(Check));
    Run(nameof(LineBreakChecks), () => LineBreakChecks.Run(Check));
    Run(nameof(ScrollChecks), () => ScrollChecks.Run(Check));
    Run(nameof(D2PolicyChecks), () => D2PolicyChecks.Run(Check));
    Run(nameof(D2Checks), () => D2Checks.Run(Check, options.D2));
    Run(nameof(AffectedChecksChecks), () => AffectedChecksChecks.Run(Check, suites.Select(s => s.Name).ToArray()));

    var names = suites.Select(s => s.Name).ToArray();
    string? root = null, statePath = null, fallback = null;
    CheckSnapshot snapshot;
    CheckHistory? history = null;
    try
    {
        root = CheckInputs.FindRoot();
        snapshot = CheckInputs.Capture(root, options.D2);
        statePath = Path.Combine(root, ".cache", "checks", "state.json");
        history = AffectedChecks.Load(statePath);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
    {
        fallback = "入力情報を取得できないため全件実行: " + ex.Message;
        snapshot = new("unavailable", new());
        Console.Error.WriteLine(fallback);
    }
    if (fallback is null && !CheckInputs.MatchesBuild(root!, snapshot))
        throw new InvalidOperationException("ソースとビルド結果が一致しません。--no-build を外し、dotnet run --project tests/ViMarkdown.Checks でビルドして実行してください。");
    var plan = AffectedChecks.Select(names, options, snapshot, history, fallback);
    Console.WriteLine($"選択: {plan.Reasons.Count}/{suites.Count} スイート" + (options.DryRun ? "（確認のみ）" : ""));
    foreach (var suite in suites.Where(s => plan.Includes(s.Name)))
        Console.WriteLine($"  {suite.Name}: {string.Join("; ", plan.Reasons[suite.Name])}");
    if (options.DryRun) return;
    if (plan.Reasons.Count == 0) { Console.WriteLine("変更により必要となるテストはありません。全件実行は --all を指定してください。"); return; }

    Environment.SetEnvironmentVariable("VIMARKDOWN_DB", ":memory:");
    // Selection and dry-run need no native window or font initialization.
    AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .SetupWithoutStarting();
    foreach (var suite in suites.Where(s => plan.Includes(s.Name)))
    {
        suiteName = suite.Name;
        var before = checks;
        suite.Body();
        Console.WriteLine($"{suite.Name}: {checks - before} checks passed.");
    }
    Console.WriteLine($"{checks} checks passed.");
    if (fallback is not null) return;
    try
    {
        var after = CheckInputs.Capture(root!, options.D2);
        if (!CheckInputs.Same(snapshot, after))
        {
            Console.Error.WriteLine("実行中に入力・実行条件が変更されたため成功記録は更新しません。再実行してください。");
            return;
        }
        AffectedChecks.Save(statePath!, AffectedChecks.RecordSuccess(names, plan, snapshot, history));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
    { Console.Error.WriteLine("成功記録を更新できません（次回再検証）: " + ex.Message); }
}

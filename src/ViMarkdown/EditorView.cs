using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Styling;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private Grid _editorArea = null!;

    private static readonly Thickness SourceEditorPadding = new(5, 24, 24, 24);

    private TextEditor CreateSourceEditor()
    {
        var editor = new TextEditor
        {
            Document = _pageContent,
            ShowLineNumbers = true,
            WordWrap = true,
            FontFamily = Typography.BodyFont,
            FontSize = Typography.InsertBody,
            Background = Background,
            Foreground = WorkspaceColors.Text,
            Padding = SourceEditorPadding,
            UseLayoutRounding = false,
            IsVisible = false
        };
        // Let the Markdown spans color URLs too; automatic URL elements would
        // otherwise override their colors, including inside code blocks.
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        var markdown = new LightPlusMarkdown();
        editor.TextArea.TextView.LineTransformers.Add(markdown);
        editor.TextArea.TextView.BackgroundRenderers.Add(markdown);
        editor.TextArea.TextView.LineTransformers.Add(new SearchHighlight(() => _displaySearchRanges));
        var lineNumbers = editor.TextArea.LeftMargins.OfType<AvaloniaEdit.Editing.LineNumberMargin>().Single();
        const double lineNumberGap = 6;
        // Move the gutter into the existing left padding, returning the same
        // width after the numbers so the text origin stays unchanged.
        var activeLineNumbers = new ActiveLineNumberMargin(editor.TextArea)
        {
            MinWidthInDigits = lineNumbers.MinWidthInDigits,
            Margin = new Thickness(0, 0, lineNumberGap, 0)
        };
        editor.TextArea.LeftMargins[editor.TextArea.LeftMargins.IndexOf(lineNumbers)] = activeLineNumbers;
        // Space on the text side of the gutter separator is included in the
        // measured margins shared with the normal and visual modes.
        foreach (var separator in editor.TextArea.LeftMargins.OfType<Avalonia.Controls.Shapes.Line>())
        {
            var margin = separator.Margin;
            separator.Margin = new Thickness(margin.Left, margin.Top, (margin.Right + 8) * 2, margin.Bottom);
        }
        editor.TextArea.TextView.BackgroundRenderers.Add(new InsertLineUnderline(editor.TextArea));
        editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            activeLineNumbers.InvalidateVisual();
            editor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);
        };
        // Hidden editors have not inherited their template fonts yet. Give the
        // gutters their rendering fonts now so widths are correct on launch.
        foreach (var margin in editor.TextArea.LeftMargins)
        {
            margin.SetValue(Avalonia.Controls.Documents.TextElement.FontFamilyProperty, editor.FontFamily);
            margin.SetValue(Avalonia.Controls.Documents.TextElement.FontSizeProperty,
                margin is ActiveLineNumberMargin ? Typography.LineNumber : editor.FontSize);
        }
        editor.Options.AllowScrollBelowDocument = false;
        editor.LineNumbersForeground = WorkspaceColors.Muted;
        editor.TextArea.SelectionBrush = WorkspaceColors.Selection;
        editor.TextArea.SelectionForeground = WorkspaceColors.Text;
        editor.TextArea.Caret.CaretBrush = WorkspaceColors.Accent;
        ConfigureEditorContextMenu(editor);
        return editor;
    }

    private void ConfigureCommandInputAppearance()
    {
        _commandInput.Background = WorkspaceColors.Chrome;
        _commandInput.BorderBrush = Brushes.Transparent;
        _commandInput.FontSize = _status.FontSize;
        _commandInputLabel.FontSize = _status.FontSize;
        // Fluent changes template borders on hover/focus independently of TextBox properties.
        // Scope these overrides to the command box so native text/IME handling stays intact.
        foreach (var state in new[] { "", ":pointerover", ":focus", ":focus-within", ":disabled" })
        {
            var border = new Style(selector =>
            {
                var box = selector.OfType<TextBox>().Name("VimCommandInput");
                if (state.Length > 0) box = box.Class(state);
                return box.Template().OfType<Border>();
            });
            border.Setters.Add(new Setter(Border.BackgroundProperty, WorkspaceColors.Chrome));
            border.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.Transparent));
            border.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(0)));
            _commandInput.Styles.Add(border);
        }
        var text = new Style(s => s.OfType<TextBox>().Name("VimCommandInput")
            .Template().OfType<TextPresenter>());
        text.Setters.Add(new Setter(Avalonia.Controls.Documents.TextElement.FontSizeProperty, _status.FontSize));
        _commandInput.Styles.Add(text);
    }

    private Control BuildEditorPane()
    {
        var root = new DockPanel { Background = WorkspaceColors.Editor };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(10, 5) };
        foreach (var (label, mode) in new[] { ("NORMAL (Esc)", EditorMode.Normal), ("VISUAL (v)", EditorMode.Visual), ("INSERT (i)", EditorMode.Insert) })
        {
            var icon = mode == EditorMode.Normal ? "normal" : mode == EditorMode.Visual ? "visual" : "insert";
            var name = mode == EditorMode.Normal ? "ノーマルモード" : mode == EditorMode.Visual ? "選択モード" : "挿入モード";
            var button = ToolbarButtons.Create(icon, name, $"{name} — {label}", label);
            _modeButtons[mode] = button;
            button.Click += (_, _) => { if (mode == EditorMode.Visual) _visualKind = 0; SwitchMode(mode); };
            toolbar.Children.Add(button);
        }
        var undo = ToolbarButtons.Create("undo", "元に戻す", "元に戻す (u)", "Undo");
        undo.Click += (_, _) => ApplyHistory(redo: false);
        toolbar.Children.Add(undo);
        var redo = ToolbarButtons.Create("redo", "やり直す", "やり直す (Ctrl+R)", "Redo");
        redo.Click += (_, _) => ApplyHistory(redo: true);
        toolbar.Children.Add(redo);
        ConfigureMediaPaste();
        var toolbarLayout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), MinWidth = MinimumEditorWidth };
        toolbarLayout.Children.Add(toolbar);
        var search = BuildSearchBox();
        Grid.SetColumn(search, 2); toolbarLayout.Children.Add(search);
        var toolbarRow = new ScrollViewer { Name = "EditorToolbarScroll", Focusable = false, IsTabStop = false, Content = toolbarLayout, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var commandLayout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        _commandInputLabel.FontSize = 12;
        _commandInputLabel.VerticalAlignment = VerticalAlignment.Center;
        _commandInputLabel.Foreground = WorkspaceColors.Accent;
        _commandInputLabel.Margin = new Thickness(0, 0, 8, 0);
        commandLayout.Children.Add(_commandInputLabel);
        Grid.SetColumn(_commandInput, 1);
        commandLayout.Children.Add(_commandInput);
        _commandInputRow.Background = Brushes.Transparent;
        _commandInputRow.Padding = new Thickness(0);
        _commandInputRow.Child = commandLayout;

        var toolbarHeader = new Border
        {
            Background = WorkspaceColors.Chrome,
            BorderBrush = WorkspaceColors.Divider,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = toolbarRow
        };
        DockPanel.SetDock(toolbarHeader, Dock.Top); root.Children.Add(toolbarHeader);
        var statusItems = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        _status.FontSize = 12;
        ConfigureCommandInputAppearance();
        _status.Foreground = WorkspaceColors.Muted;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _saveStatus.FontSize = 12;
        _saveStatus.VerticalAlignment = VerticalAlignment.Center;
        statusItems.Children.Add(_status);
        statusItems.Children.Add(new Border
        {
            Width = 1,
            Height = 14,
            Background = WorkspaceColors.Divider,
            VerticalAlignment = VerticalAlignment.Center
        });
        // Share the save-state slot while retaining its latest text during
        // the conversion notification (including automatic saves).
        _conversionStatusDisplay.Foreground = _saveStatus.Foreground;
        _conversionStatusDisplay.FontSize = _saveStatus.FontSize;
        _conversionStatusDisplay.VerticalAlignment = _saveStatus.VerticalAlignment;
        var saveStatusSlot = new Grid();
        saveStatusSlot.Children.Add(_saveStatus);
        saveStatusSlot.Children.Add(_conversionStatusDisplay);
        statusItems.Children.Add(saveStatusSlot);
        var slideshow = ToolbarButtons.Create("slideshow", "スライドショー", "スライドショー (F5) — Escで終了");
        slideshow.Name = "SlideshowButton";
        // Match the existing text row; the standard toolbar button is too tall.
        slideshow.Width = slideshow.MinWidth = 22;
        slideshow.Height = slideshow.MinHeight = 0;
        slideshow.Padding = new Thickness(0);
        slideshow.ClipToBounds = true;
        // Let the existing status text determine the row height before sizing
        // the button, so adding it cannot make the status bar taller.
        statusItems.SizeChanged += (_, e) =>
        {
            slideshow.Height = Math.Min(18, e.NewSize.Height);
            if (slideshow.Content is Avalonia.Controls.Shapes.Path icon)
                icon.Width = icon.Height = Math.Min(16, slideshow.Height);
        };
        slideshow.VerticalAlignment = VerticalAlignment.Center;
        slideshow.Click += (_, _) => StartSlideshow();
        var statusLayout = _normalStatus = new Grid { Name = "EditorStatusDisplay", ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        statusLayout.Children.Add(new ScrollViewer
        {
            Content = statusItems,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        });
        _commandInput.TextChanged += (_, _) =>
        {
            if (_commandPrompt is not null && _commandInput.IsVisible)
                _commandPrompt = _commandPrompt[..1] + (_commandInput.Text ?? "");
        };
        Grid.SetColumn(slideshow, 1);
        statusLayout.Children.Add(slideshow);
        var statusHost = new Grid();
        statusHost.Children.Add(statusLayout);
        statusHost.Children.Add(_commandInputRow);
        // Reuse the measured status height, including while its normal content is hidden.
        statusLayout.SizeChanged += (_, e) =>
        {
            if (statusLayout.IsVisible && e.NewSize.Height > 0)
                statusHost.Height = e.NewSize.Height;
        };
        var statusBar = new Border
        {
            Background = WorkspaceColors.Chrome,
            BorderBrush = WorkspaceColors.Divider,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 5),
            Child = statusHost
        };
        DockPanel.SetDock(statusBar, Dock.Bottom); root.Children.Add(statusBar);
        var area = _editorArea = new Grid { Focusable = true }; area.Children.Add(_preview); area.Children.Add(_editor);
        root.Children.Add(new Border
        {
            Background = WorkspaceColors.Editor,
            BorderBrush = WorkspaceColors.Divider,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Margin = new Thickness(0),
            ClipToBounds = true,
            Child = WithPaneFocus(area, 3)
        });
        root.Background = WorkspaceColors.Canvas;
        return root;
    }
}

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using ViMarkdown;

// Each scenario owns its window and database, including when an assertion fails.
internal sealed class EditorSession : IDisposable
{
    public MainWindow Window { get; }
    public TextEditor Editor { get; }
    public RichSurface Rich { get; }

    public EditorSession(string databasePath = ":memory:", double? height = null)
    {
        Window = new MainWindow(databasePath);
        if (height.HasValue) Window.Height = height.Value;
        try
        {
            Window.Show();
            Dispatcher.UIThread.RunJobs();
            Editor = Window.GetVisualDescendants().OfType<TextEditor>().Single();
            // An unselected page hides the preview before its ScrollViewer
            // template is created, so it need not have visual descendants yet.
            Rich = Window.GetLogicalDescendants().OfType<RichSurface>().Single();
        }
        catch
        {
            Window.Close();
            throw;
        }
    }

    public void Click(string tag, bool focus = false)
    {
        var button = Window.GetVisualDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == tag);
        if (focus) button.Focus();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    public void Key(PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var symbol = key.ToQwertyKeySymbol(modifiers.HasFlag(RawInputModifiers.Shift));
        Window.KeyPress(key.ToQwertyKey(), modifiers, key, symbol);
        if (Window.IsVisible) Window.KeyRelease(key.ToQwertyKey(), modifiers, key, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    public void Keys(string keys)
    {
        foreach (var c in keys)
        {
            if (char.IsAsciiLetter(c))
                Key(Enum.Parse<PhysicalKey>(char.ToUpperInvariant(c).ToString()),
                    char.IsUpper(c) ? RawInputModifiers.Shift : RawInputModifiers.None);
            else if (char.IsAsciiDigit(c)) Key(Enum.Parse<PhysicalKey>("Digit" + c));
            else
            {
                var (key, shift) = c switch
                {
                    '$' => (PhysicalKey.Digit4, true),
                    '^' => (PhysicalKey.Digit6, true),
                    '(' => (PhysicalKey.Digit9, true),
                    ')' => (PhysicalKey.Digit0, true),
                    '{' => (PhysicalKey.BracketLeft, true),
                    '[' => (PhysicalKey.BracketLeft, false),
                    ']' => (PhysicalKey.BracketRight, false),
                    '`' => (PhysicalKey.Backquote, false),
                    '}' => (PhysicalKey.BracketRight, true),
                    '"' => (PhysicalKey.Quote, true),
                    '\'' => (PhysicalKey.Quote, false),
                    '>' => (PhysicalKey.Period, true),
                    '<' => (PhysicalKey.Comma, true),
                    ' ' => (PhysicalKey.Space, false),
                    '=' => (PhysicalKey.Equal, false),
                    ':' => (PhysicalKey.Semicolon, true),
                    '/' => (PhysicalKey.Slash, false),
                    '?' => (PhysicalKey.Slash, true),
                    '*' => (PhysicalKey.Digit8, true),
                    '#' => (PhysicalKey.Digit3, true),
                    _ => throw new ArgumentException("Unsupported test key " + c, nameof(keys))
                };
                Key(key, shift ? RawInputModifiers.Shift : RawInputModifiers.None);
            }
        }
    }

    public void Reset(string text, int offset = 0)
    {
        Key(PhysicalKey.Escape);
        Click("INSERT (i)");
        Editor.Text = text;
        Editor.CaretOffset = TextBoundaries.Next(text, offset);
        Editor.Document.UndoStack.ClearAll();
        Key(PhysicalKey.Escape);
    }

    public void Prompt(string opener, string text)
    {
        Keys(opener);
        Window.KeyTextInput(text);
        Key(PhysicalKey.Enter);
    }

    public void Dispose() => Window.Close();
}

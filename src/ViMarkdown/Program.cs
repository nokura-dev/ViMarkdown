using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Avalonia.Markup.Xaml.Styling;

namespace ViMarkdown;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => AppBuilder.Configure<App>()
        .UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}

public sealed class App : Application
{
    public App()
    {
        Name = "ViMarkdown";
        var aboutItem = new NativeMenuItem("About ViMarkdown");
        aboutItem.Click += async (_, _) =>
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
                { MainWindow: { IsVisible: true } mainWindow } && !mainWindow.OwnedWindows.Any(w => w.IsVisible))
                await HelpDialogs.ShowAbout(mainWindow);
        };
        NativeMenu.SetMenu(this, new NativeMenu { aboutItem });
    }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        // Fluent reserves 16px at either end for line buttons even while their
        // icons are hidden. Let vertical thumbs use the entire pane height.
        foreach (var name in new[] { "PART_LineUpButton", "PART_LineDownButton" })
            Styles.Add(new Style(x => x.OfType<ScrollBar>().Class(":vertical")
                .Template().OfType<RepeatButton>().Name(name))
            {
                Setters = { new Setter(Visual.IsVisibleProperty, false) }
            });
        Styles.Add(new Style(x => x.OfType<ScrollViewer>())
        {
            Setters = { new Setter(ScrollActivity.EnabledProperty, true) }
        });
        // AvaloniaEdit supplies its own templates; FluentTheme alone does not
        // create TextEditor/TextArea's visual tree.
        Styles.Add(new StyleInclude(new Uri("avares://ViMarkdown/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml")
        });
        // Include separate dialog/tooltip roots as well as the main window.
        var controlTypography = new Style(x => x.Is<TemplatedControl>())
        {
            Setters = { new Setter(TemplatedControl.FontSizeProperty, Typography.Ui) }
        };
        var textTypography = new Style(x => x.OfType<TextBlock>())
        {
            Setters = { new Setter(TextBlock.FontSizeProperty, Typography.Ui) }
        };
        controlTypography.Setters.Add(new Setter(TemplatedControl.FontFamilyProperty, Typography.BodyFont));
        textTypography.Setters.Add(new Setter(TextBlock.FontFamilyProperty, Typography.BodyFont));
        Styles.Add(controlTypography);
        Styles.Add(textTypography);
        RequestedThemeVariant = ThemeVariant.Light;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

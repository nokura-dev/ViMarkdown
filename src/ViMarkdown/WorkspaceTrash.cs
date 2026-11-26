using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private async System.Threading.Tasks.Task ShowTrash()
    {
        var dialog = new Window
        {
            Title = "ごみ箱", Width = 680, Height = 460, MinWidth = 420, MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = WorkspaceColors.Editor, Foreground = WorkspaceColors.Text
        };
        var layout = new DockPanel { Margin = new Thickness(12) };
        var help = new TextBlock { Text = "削除から30日経過したページは、アプリ起動時に完全削除されます。「削除」を押すと、選択したページを即時に完全削除します。", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(help, Dock.Top); layout.Children.Add(help);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var restore = new Button { Content = "復元", IsEnabled = false };
        var delete = new Button { Content = "削除", IsEnabled = false };
        var close = new Button { Content = "閉じる" };
        footer.Children.Add(restore); footer.Children.Add(delete); footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,2*"), ColumnSpacing = 12 };
        var list = new ListBox { Name = "TrashPages", Background = WorkspaceColors.Pages, Foreground = WorkspaceColors.Text };
        var details = new DockPanel();
        var date = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(date, Dock.Top); details.Children.Add(date);
        var body = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Background = WorkspaceColors.Editor, Foreground = WorkspaceColors.Text };
        details.Children.Add(body);
        Grid.SetColumn(details, 1); grid.Children.Add(list); grid.Children.Add(details); layout.Children.Add(grid);
        void Reload()
        {
            list.ItemsSource = _store.TrashItems();
            list.SelectedIndex = list.ItemCount > 0 ? 0 : -1;
        }
        list.SelectionChanged += (_, _) =>
        {
            var page = list.SelectedItem as WorkspaceItem;
            restore.IsEnabled = page is not null;
            delete.IsEnabled = page is not null;
            date.Text = page?.DeletedAt is { } deleted ? "削除日: " + deleted.ToLocalTime().ToString(PageNaming.TimestampFormat) : "ごみ箱は空です";
            body.Text = page?.Body ?? "";
        };
        restore.Click += (_, _) =>
        {
            if (list.SelectedItem is not WorkspaceItem page) return;
            try { _store.Restore(page.Id); ReloadLists(); Reload(); }
            catch (Exception ex) { date.Text = "復元失敗: " + ex.Message; }
        };
        close.Click += (_, _) => dialog.Close();
        delete.Click += (_, _) =>
        {
            if (list.SelectedItem is not WorkspaceItem page) return;
            try { _store.PermanentlyDelete(page.Id); Reload(); }
            catch (Exception ex) { date.Text = "削除失敗: " + ex.Message; }
        };
        dialog.Content = layout;
        Reload();
        if (list.ItemCount == 0) date.Text = "ごみ箱は空です";
        await dialog.ShowDialog(this);
    }
}

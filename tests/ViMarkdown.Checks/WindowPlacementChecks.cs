using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ViMarkdown;

internal static class WindowPlacementChecks
{
    public static void Run(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ViMarkdown-placement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "workspace.db");
        try
        {
            using (var session = new EditorSession(path))
            {
                session.Window.Width = 1000;
                session.Window.Height = 600;
                session.Window.Position = new PixelPoint(40, 50);
                Dispatcher.UIThread.RunJobs();
            }
            using (var session = new EditorSession(path))
            {
                check(session.Window.Width == 1000 && session.Window.Height == 600, "再起動時にサイズを復元する");
                check(session.Window.Position == new PixelPoint(40, 50), "再起動時に位置を復元する");
                session.Window.WindowState = WindowState.Maximized;
            }
            using (var session = new EditorSession(path))
            {
                check(session.Window.WindowState == WindowState.Maximized, "最大化状態を復元する");
            }
            using (var store = new WorkspaceStore(path))
                store.SetSetting("window-placement", "invalid json");
            using (var session = new EditorSession(path))
                check(session.Window.Width == 1400 && session.Window.Height == 760, "設定破損時は初期サイズで起動する");
            using (var store = new WorkspaceStore(path))
                store.SetSetting("window-placement", "{\"X\":0,\"Y\":0,\"Width\":500,\"Height\":200,\"Maximized\":false}");
            using (var session = new EditorSession(path))
                check(session.Window.Width >= session.Window.MinWidth && session.Window.Height >= session.Window.MinHeight,
                    "以前保存した小さいウィンドウも現在の最小幅・高さ以上で復元する");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }
}

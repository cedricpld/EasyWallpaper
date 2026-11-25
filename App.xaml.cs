using System.Windows;

namespace CGWallpaper;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(a => a.Equals("--test", StringComparison.OrdinalIgnoreCase)))
        {
            var monitors = DesktopLayer.GetMonitors();
            Console.WriteLine($"Monitors detected: {monitors.Count}");
            foreach (var m in monitors)
            {
                Console.WriteLine($"  - {m.Title} at ({m.Left},{m.Top})");
            }
            Shutdown(0);
            return;
        }

        bool startMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        var main = new MainWindow(startMinimized);
        MainWindow = main;
        if (!startMinimized)
        {
            main.Show();
        }
    }
}

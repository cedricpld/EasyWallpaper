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
        string? importFile = e.Args.FirstOrDefault(a =>
            (a.EndsWith(".ewp", StringComparison.OrdinalIgnoreCase) || a.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            && System.IO.File.Exists(a));

        var main = new MainWindow(startMinimized, importFile);
        MainWindow = main;
        if (!startMinimized)
        {
            main.Show();
        }
    }
}

using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;

namespace CGWallpaper;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Local\\EasyWallpaper_SingleInstance_Mutex";
    private const string PipeName = "EasyWallpaper_IPC_Pipe";
    private static Mutex? _instanceMutex;
    private static CancellationTokenSource? _pipeCts;

    public static void ReleaseSingleInstance()
    {
        _pipeCts?.Cancel();
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        _instanceMutex?.Dispose();
        _instanceMutex = null;
    }

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

        string? importFile = e.Args.FirstOrDefault(a =>
            (a.EndsWith(".ewp", StringComparison.OrdinalIgnoreCase) || a.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            && File.Exists(a));

        _instanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            NotifyRunningInstance(importFile);
            Shutdown(0);
            return;
        }

        bool startMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        var main = new MainWindow(startMinimized, importFile);
        MainWindow = main;
        if (!startMinimized)
        {
            main.Show();
        }

        StartIpcServer(main);
    }

    private static void NotifyRunningInstance(string? importFile)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(string.IsNullOrEmpty(importFile) ? "ACTIVATE" : $"IMPORT|{importFile}");
        }
        catch { }
    }

    private static void StartIpcServer(MainWindow main)
    {
        _pipeCts = new CancellationTokenSource();
        var ct = _pipeCts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(ct);
                    using var reader = new StreamReader(server);
                    string? msg = await reader.ReadLineAsync(ct);
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        main.Dispatcher.Invoke(() => main.HandleIpcMessage(msg));
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { await Task.Delay(200, ct); }
            }
        }, ct);
    }
}

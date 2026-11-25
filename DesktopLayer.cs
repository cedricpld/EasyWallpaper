using System.IO;
using System.Runtime.InteropServices;

namespace CGWallpaper;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[ComImport]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDesktopWallpaper
{
    void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
    void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
    void GetMonitorDevicePathAt(uint monitorIndex, [MarshalAs(UnmanagedType.LPWStr)] out string monitorID);
    void GetMonitorDevicePathCount(out uint count);
    void GetMonitorRect([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out RECT displayRect);
    void SetBackgroundColor(uint color);
    void GetBackgroundColor(out uint color);
    void SetPosition(int position);
    void GetPosition(out int position);
    void SetSlideshow(IntPtr items);
    void GetSlideshow(out IntPtr items);
    void SetSlideshowOptions(uint options, uint slideshowTick);
    void GetSlideshowOptions(out uint options, out uint slideshowTick);
    void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, uint direction);
    void GetStatus(out uint state);
    void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
}

[ComImport]
[Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
public class DesktopWallpaperClass { }

public static class DesktopLayer
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CHILD = 0x40000000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint LWA_ALPHA = 0x00000002;

    public static List<MonitorInfoItem> GetMonitors()
    {
        var list = new List<MonitorInfoItem>();
        var dw = (IDesktopWallpaper)new DesktopWallpaperClass();
        dw.GetMonitorDevicePathCount(out uint count);

        for (uint i = 0; i < count; i++)
        {
            dw.GetMonitorDevicePathAt(i, out string monId);
            try
            {
                dw.GetMonitorRect(monId, out RECT r);
                int w = r.Right - r.Left;
                int h = r.Bottom - r.Top;
                if (w <= 0 || h <= 0) continue;

                list.Add(new MonitorInfoItem
                {
                    Index = (int)i + 1,
                    MonitorId = monId,
                    Left = r.Left,
                    Top = r.Top,
                    Width = w,
                    Height = h,
                    IsPrimary = r.Left == 0 && r.Top == 0
                });
            }
            catch
            {
                // Disconnected monitor
            }
        }
        return list;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    private const uint SWP_ASYNCWINDOWPOS = 0x4000;

    public static void EnsureZOrder(IntPtr hwnd)
    {
        IntPtr progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return;

        IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        bool defOk = false;
        if (defView != IntPtr.Zero)
        {
            defOk = SetWindowPos(hwnd, defView, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_ASYNCWINDOWPOS);
        }

        var zList = new List<string>();
        EnumChildWindows(progman, (child, _) =>
        {
            var sb = new System.Text.StringBuilder(64);
            GetClassName(child, sb, 64);
            string cls = sb.ToString();
            if (cls == "WorkerW")
            {
                // HWND_BOTTOM (1) puts WorkerW behind ALL our host windows at once
                bool wOk = SetWindowPos(child, new IntPtr(1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
                zList.Add($"{cls}:{child}(moved={wOk},err={Marshal.GetLastWin32Error()})");
            }
            else
            {
                zList.Add($"{cls}:{child}" + (child == hwnd ? "[OURS]" : ""));
            }
            return true;
        }, IntPtr.Zero);

        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CGWallpaper");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "debug.log"),
                $"{DateTime.Now:HH:mm:ss} ZOrder defOk={defOk} children=[{string.Join(", ", zList)}]\r\n");
        }
        catch { }
    }

    public static (int relX, int relY) AttachToDesktop(IntPtr hwnd, int absX, int absY, int width, int height)
    {
        IntPtr progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return (absX, absY);

        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), 0x0000, 1000, out _);

        IntPtr workerW = IntPtr.Zero;
        EnumWindows((topHandle, _) =>
        {
            IntPtr p = FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (p != IntPtr.Zero)
            {
                workerW = FindWindowEx(IntPtr.Zero, topHandle, "WorkerW", null);
            }
            return true;
        }, IntPtr.Zero);

        IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        IntPtr childWorkerW = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);

        IntPtr targetParent = workerW != IntPtr.Zero ? workerW : progman;
        int relX = absX;
        int relY = absY;
        if (GetWindowRect(targetParent, out RECT parentRect))
        {
            relX = absX - parentRect.Left;
            relY = absY - parentRect.Top;
        }

        // Match Lively Wallpaper exact WinForms host styles on Windows 11 26H2
        SetWindowLong(hwnd, GWL_EXSTYLE, 0x08090080); // WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_CONTROLPARENT | WS_EX_TOOLWINDOW
        SetWindowLong(hwnd, GWL_STYLE, 0x56010000);   // WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS | WS_TABSTOP

        SetParent(hwnd, targetParent);
        SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);

        if (workerW != IntPtr.Zero)
        {
            SetWindowPos(hwnd, IntPtr.Zero, relX, relY, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
        }
        else
        {
            if (defView != IntPtr.Zero)
            {
                SetWindowPos(hwnd, defView, relX, relY, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
            }
            else
            {
                SetWindowPos(hwnd, IntPtr.Zero, relX, relY, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
            }

            if (childWorkerW != IntPtr.Zero)
            {
                SetWindowPos(childWorkerW, hwnd, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        try
        {
            GetWindowRect(hwnd, out RECT actual);
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CGWallpaper");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "debug.log"),
                $"{DateTime.Now:HH:mm:ss} WPF parent={targetParent} rel=({relX},{relY},{width}x{height}) actual=({actual.Left},{actual.Top},{actual.Right - actual.Left}x{actual.Bottom - actual.Top}) vis={IsWindowVisible(hwnd)}\r\n");
        }
        catch { }

        return (relX, relY);
    }
}

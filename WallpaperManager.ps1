param([switch]$TestOnly)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$csharpCode = @"
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

[StructLayout(LayoutKind.Sequential)]
public struct RECT {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

public enum DESKTOP_WALLPAPER_POSITION {
    DWPOS_CENTER = 0,
    DWPOS_TILE = 1,
    DWPOS_STRETCH = 2,
    DWPOS_FIT = 3,
    DWPOS_FILL = 4,
    DWPOS_SPAN = 5
}

[ComImport]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDesktopWallpaper {
    void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
    void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
    void GetMonitorDevicePathAt(uint monitorIndex, [MarshalAs(UnmanagedType.LPWStr)] out string monitorID);
    void GetMonitorDevicePathCount(out uint count);
    void GetMonitorRect([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out RECT displayRect);
    void SetBackgroundColor(uint color);
    void GetBackgroundColor(out uint color);
    void SetPosition(DESKTOP_WALLPAPER_POSITION position);
    void GetPosition(out DESKTOP_WALLPAPER_POSITION position);
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

public static class WallpaperHelper {
    private static IDesktopWallpaper Create() {
        return (IDesktopWallpaper)new DesktopWallpaperClass();
    }
    public static uint GetMonitorCount() {
        uint count;
        Create().GetMonitorDevicePathCount(out count);
        return count;
    }
    public static string GetMonitorId(uint index) {
        string id;
        Create().GetMonitorDevicePathAt(index, out id);
        return id;
    }
    public static RECT GetMonitorRect(string monitorId) {
        RECT rect;
        Create().GetMonitorRect(monitorId, out rect);
        return rect;
    }
    public static string GetWallpaper(string monitorId) {
        string path;
        Create().GetWallpaper(monitorId, out path);
        return path;
    }
    public static void SetWallpaper(string monitorId, string path) {
        Create().SetWallpaper(monitorId, path);
    }
    public static DESKTOP_WALLPAPER_POSITION GetPosition() {
        DESKTOP_WALLPAPER_POSITION pos;
        Create().GetPosition(out pos);
        return pos;
    }
    public static void SetPosition(DESKTOP_WALLPAPER_POSITION pos) {
        Create().SetPosition(pos);
    }
}

public static class DesktopLayerHelper {
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

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
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    private const int GWL_STYLE = -16;
    private const int WS_CHILD = 0x40000000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint LWA_ALPHA = 0x00000002;

    public static void AttachToDesktop(IntPtr formHandle, int absX, int absY, int width, int height) {
        IntPtr progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return;

        IntPtr result;
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), 0x0000, 1000, out result);

        IntPtr workerW = IntPtr.Zero;
        EnumWindows((topHandle, lp) => {
            IntPtr p = FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (p != IntPtr.Zero) {
                workerW = FindWindowEx(IntPtr.Zero, topHandle, "WorkerW", null);
            }
            return true;
        }, IntPtr.Zero);

        IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        IntPtr childWorkerW = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);

        int style = GetWindowLong(formHandle, GWL_STYLE);
        SetWindowLong(formHandle, GWL_STYLE, (style & ~WS_POPUP) | WS_CHILD);

        IntPtr targetParent = (workerW != IntPtr.Zero) ? workerW : progman;
        RECT parentRect;
        int relX = absX;
        int relY = absY;
        if (GetWindowRect(targetParent, out parentRect)) {
            relX = absX - parentRect.Left;
            relY = absY - parentRect.Top;
        }

        IntPtr prevParent = SetParent(formHandle, targetParent);
        int errParent = Marshal.GetLastWin32Error();

        bool posOk = false;
        if (workerW != IntPtr.Zero) {
            posOk = SetWindowPos(formHandle, IntPtr.Zero, relX, relY, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
        } else {
            if (defView != IntPtr.Zero) {
                posOk = SetWindowPos(formHandle, defView, relX, relY, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
            } else {
                posOk = SetWindowPos(formHandle, IntPtr.Zero, relX, relY, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
            }
            if (childWorkerW != IntPtr.Zero) {
                SetWindowPos(childWorkerW, formHandle, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }
        int errPos = Marshal.GetLastWin32Error();
        RedrawWindow(formHandle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0100);

        try {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CGWallpaper");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "debug.log"),
                string.Format("{0:HH:mm:ss} parent={1} prevParent={2}(err={3}) posOk={4}(err={5}) vis={6} rel=({7},{8},{9}x{10})\r\n",
                DateTime.Now, targetParent, prevParent, errParent, posOk, errPos, IsWindowVisible(formHandle), relX, relY, width, height));
        } catch {}
    }
}

public class WallpaperHostForm : System.Windows.Forms.Form {
    public System.Drawing.Image WallpaperImage { get; set; }
    public DESKTOP_WALLPAPER_POSITION FitMode { get; set; }

    public WallpaperHostForm() {
        this.DoubleBuffered = true;
        this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.BackColor = System.Drawing.Color.Black;
        this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
    }

    protected override System.Windows.Forms.CreateParams CreateParams {
        get {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
            return cp;
        }
    }

    protected override bool ShowWithoutActivation {
        get { return true; }
    }

    protected override void OnPaint(System.Windows.Forms.PaintEventArgs e) {
        base.OnPaint(e);
        if (WallpaperImage == null) return;
        var g = e.Graphics;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        int cw = this.ClientSize.Width;
        int ch = this.ClientSize.Height;
        int iw = WallpaperImage.Width;
        int ih = WallpaperImage.Height;
        if (cw <= 0 || ch <= 0 || iw <= 0 || ih <= 0) return;

        if (FitMode == DESKTOP_WALLPAPER_POSITION.DWPOS_STRETCH) {
            g.DrawImage(WallpaperImage, 0, 0, cw, ch);
        } else if (FitMode == DESKTOP_WALLPAPER_POSITION.DWPOS_CENTER) {
            g.DrawImage(WallpaperImage, (cw - iw) / 2, (ch - ih) / 2, iw, ih);
        } else if (FitMode == DESKTOP_WALLPAPER_POSITION.DWPOS_TILE) {
            using (var brush = new System.Drawing.TextureBrush(WallpaperImage)) {
                g.FillRectangle(brush, 0, 0, cw, ch);
            }
        } else if (FitMode == DESKTOP_WALLPAPER_POSITION.DWPOS_FIT) {
            double scale = Math.Min((double)cw / iw, (double)ch / ih);
            int dw = (int)(iw * scale);
            int dh = (int)(ih * scale);
            g.DrawImage(WallpaperImage, (cw - dw) / 2, (ch - dh) / 2, dw, dh);
        } else {
            // DWPOS_FILL / DWPOS_SPAN -> cover
            double scale = Math.Max((double)cw / iw, (double)ch / ih);
            int dw = (int)(iw * scale);
            int dh = (int)(ih * scale);
            g.DrawImage(WallpaperImage, (cw - dw) / 2, (ch - dh) / 2, dw, dh);
        }
    }
}

public class WebcamResult {
    public string MonitorId;
    public string FilePath;
    public string Error;
}

public static class WebcamFetcher {
    private static readonly ConcurrentQueue<WebcamResult> _results = new ConcurrentQueue<WebcamResult>();
    private static readonly ConcurrentDictionary<string, bool> _inFlight = new ConcurrentDictionary<string, bool>();

    static WebcamFetcher() {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
    }

    public static bool TryDequeue(out WebcamResult result) {
        return _results.TryDequeue(out result);
    }

    public static void FetchAsync(string monitorId, string url, string destPath) {
        if (!_inFlight.TryAdd(monitorId, true)) return;
        Task.Run(() => {
            var res = new WebcamResult { MonitorId = monitorId, FilePath = destPath };
            try {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                req.Headers.Add("Cache-Control", "no-cache");

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var stream = resp.GetResponseStream()) {
                    string ct = (resp.ContentType ?? "").ToLowerInvariant();
                    string dir = Path.GetDirectoryName(destPath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    if (ct.Contains("multipart/x-mixed-replace")) {
                        byte[] frame = ReadMjpegFrame(stream, 5 * 1024 * 1024);
                        if (frame == null || frame.Length == 0) {
                            throw new Exception("Flux MJPEG : aucune image JPEG trouvée.");
                        }
                        File.WriteAllBytes(destPath, frame);
                    } else {
                        using (var ms = new MemoryStream()) {
                            stream.CopyTo(ms);
                            byte[] data = ms.ToArray();
                            if (data.Length < 100) throw new Exception("Réponse image vide ou invalide.");
                            File.WriteAllBytes(destPath, data);
                        }
                    }
                }
            } catch (Exception ex) {
                res.Error = ex.Message;
            } finally {
                bool removed;
                _inFlight.TryRemove(monitorId, out removed);
                _results.Enqueue(res);
            }
        });
    }

    private static byte[] ReadMjpegFrame(Stream stream, int maxBytes) {
        var buf = new List<byte>(65536);
        int prev = -1;
        bool inJpeg = false;
        int b;
        int totalRead = 0;
        while (totalRead < maxBytes && (b = stream.ReadByte()) != -1) {
            totalRead++;
            if (!inJpeg) {
                if (prev == 0xFF && b == 0xD8) {
                    inJpeg = true;
                    buf.Add(0xFF);
                    buf.Add(0xD8);
                }
            } else {
                buf.Add((byte)b);
                if (prev == 0xFF && b == 0xD9) {
                    return buf.ToArray();
                }
            }
            prev = b;
        }
        return null;
    }
}
"@

Add-Type -TypeDefinition $csharpCode -Language CSharp -ReferencedAssemblies System.Windows.Forms, System.Drawing

[System.Windows.Forms.Application]::EnableVisualStyles()

# --- Colors & Fonts ---
$bgForm      = [System.Drawing.Color]::FromArgb(24, 24, 37)
$bgCanvas    = [System.Drawing.Color]::FromArgb(17, 17, 27)
$bgCard      = [System.Drawing.Color]::FromArgb(42, 42, 60)
$bgCardHover = [System.Drawing.Color]::FromArgb(56, 56, 80)
$borderCard  = [System.Drawing.Color]::FromArgb(88, 91, 112)
$borderFocus = [System.Drawing.Color]::FromArgb(137, 180, 250)
$fgText      = [System.Drawing.Color]::FromArgb(205, 214, 244)
$fgMuted     = [System.Drawing.Color]::FromArgb(166, 173, 200)
$accentBlue  = [System.Drawing.Color]::FromArgb(59, 130, 246)
$accentGreen = [System.Drawing.Color]::FromArgb(166, 227, 161)
$accentRed   = [System.Drawing.Color]::FromArgb(243, 139, 168)

$fontTitle  = New-Object System.Drawing.Font("Segoe UI", 13, [System.Drawing.FontStyle]::Bold)
$fontNormal = New-Object System.Drawing.Font("Segoe UI", 9.5, [System.Drawing.FontStyle]::Regular)
$fontBold   = New-Object System.Drawing.Font("Segoe UI", 9.5, [System.Drawing.FontStyle]::Bold)
$fontSmall  = New-Object System.Drawing.Font("Segoe UI", 8.5, [System.Drawing.FontStyle]::Regular)

# --- Main Form ---
$form = New-Object System.Windows.Forms.Form
$form.Text = "Gestionnaire de Fonds d'Écran"
$form.Size = New-Object System.Drawing.Size(860, 590)
$form.MinimumSize = New-Object System.Drawing.Size(700, 480)
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
$form.BackColor = $bgForm
$form.ForeColor = $fgText
$form.Font = $fontNormal

# --- Header ---
$lblHeader = New-Object System.Windows.Forms.Label
$lblHeader.Text = "Cliquez sur un écran ou glissez-y une image pour définir son fond d'écran"
$lblHeader.Font = $fontTitle
$lblHeader.ForeColor = $fgText
$lblHeader.Location = New-Object System.Drawing.Point(20, 14)
$lblHeader.Size = New-Object System.Drawing.Size(800, 28)
$lblHeader.Anchor = [System.Windows.Forms.AnchorStyles]::Top -bor [System.Windows.Forms.AnchorStyles]::Left -bor [System.Windows.Forms.AnchorStyles]::Right
$form.Controls.Add($lblHeader)

# --- Monitor Layout Canvas ---
$canvas = New-Object System.Windows.Forms.Panel
$canvas.Location = New-Object System.Drawing.Point(20, 50)
$canvas.Size = New-Object System.Drawing.Size(804, 420)
$canvas.BackColor = $bgCanvas
$canvas.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
$canvas.Anchor = [System.Windows.Forms.AnchorStyles]::Top -bor [System.Windows.Forms.AnchorStyles]::Bottom -bor [System.Windows.Forms.AnchorStyles]::Left -bor [System.Windows.Forms.AnchorStyles]::Right
$form.Controls.Add($canvas)

# --- Bottom Bar ---
$bottomPanel = New-Object System.Windows.Forms.Panel
$bottomPanel.Location = New-Object System.Drawing.Point(20, 482)
$bottomPanel.Size = New-Object System.Drawing.Size(804, 56)
$bottomPanel.BackColor = $bgForm
$bottomPanel.Anchor = [System.Windows.Forms.AnchorStyles]::Bottom -bor [System.Windows.Forms.AnchorStyles]::Left -bor [System.Windows.Forms.AnchorStyles]::Right
$form.Controls.Add($bottomPanel)

$lblMode = New-Object System.Windows.Forms.Label
$lblMode.Text = "Ajustement :"
$lblMode.Location = New-Object System.Drawing.Point(0, 16)
$lblMode.Size = New-Object System.Drawing.Size(85, 24)
$lblMode.ForeColor = $fgMuted
$bottomPanel.Controls.Add($lblMode)

$cmbMode = New-Object System.Windows.Forms.ComboBox
$cmbMode.DropDownStyle = [System.Windows.Forms.ComboBoxStyle]::DropDownList
$cmbMode.Location = New-Object System.Drawing.Point(88, 13)
$cmbMode.Size = New-Object System.Drawing.Size(170, 28)
$cmbMode.BackColor = $bgCard
$cmbMode.ForeColor = $fgText
$cmbMode.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$modeItems = @(
    @{ Name = "Remplir (Fill)";   Value = [DESKTOP_WALLPAPER_POSITION]::DWPOS_FILL },
    @{ Name = "Ajuster (Fit)";    Value = [DESKTOP_WALLPAPER_POSITION]::DWPOS_FIT },
    @{ Name = "Étirer (Stretch)"; Value = [DESKTOP_WALLPAPER_POSITION]::DWPOS_STRETCH },
    @{ Name = "Centrer (Center)"; Value = [DESKTOP_WALLPAPER_POSITION]::DWPOS_CENTER },
    @{ Name = "Mosaïque (Tile)";  Value = [DESKTOP_WALLPAPER_POSITION]::DWPOS_TILE },
    @{ Name = "Étendre (Span)";   Value = [DESKTOP_WALLPAPER_POSITION]::DWPOS_SPAN }
)
foreach ($m in $modeItems) { [void]$cmbMode.Items.Add($m.Name) }
$cmbMode.SelectedIndex = 0
try {
    $currentPos = [WallpaperHelper]::GetPosition()
    for ($i = 0; $i -lt $modeItems.Count; $i++) {
        if ($modeItems[$i].Value -eq $currentPos) {
            $cmbMode.SelectedIndex = $i
            break
        }
    }
} catch {}
$bottomPanel.Controls.Add($cmbMode)

$lblStatus = New-Object System.Windows.Forms.Label
$lblStatus.Text = ""
$lblStatus.Location = New-Object System.Drawing.Point(270, 16)
$lblStatus.Size = New-Object System.Drawing.Size(290, 24)
$lblStatus.ForeColor = $accentGreen
$lblStatus.Anchor = [System.Windows.Forms.AnchorStyles]::Left -bor [System.Windows.Forms.AnchorStyles]::Right
$bottomPanel.Controls.Add($lblStatus)

$btnRefresh = New-Object System.Windows.Forms.Button
$btnRefresh.Text = "Actualiser écrans"
$btnRefresh.Location = New-Object System.Drawing.Point(568, 10)
$btnRefresh.Size = New-Object System.Drawing.Size(120, 34)
$btnRefresh.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$btnRefresh.FlatAppearance.BorderColor = $borderCard
$btnRefresh.BackColor = $bgCard
$btnRefresh.ForeColor = $fgText
$btnRefresh.Cursor = [System.Windows.Forms.Cursors]::Hand
$btnRefresh.Anchor = [System.Windows.Forms.AnchorStyles]::Right
$bottomPanel.Controls.Add($btnRefresh)

$btnApply = New-Object System.Windows.Forms.Button
$btnApply.Text = "Appliquer"
$btnApply.Font = $fontBold
$btnApply.Location = New-Object System.Drawing.Point(698, 10)
$btnApply.Size = New-Object System.Drawing.Size(106, 34)
$btnApply.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$btnApply.FlatAppearance.BorderSize = 0
$btnApply.BackColor = $accentBlue
$btnApply.ForeColor = [System.Drawing.Color]::White
$btnApply.Cursor = [System.Windows.Forms.Cursors]::Hand
$btnApply.Anchor = [System.Windows.Forms.AnchorStyles]::Right
$bottomPanel.Controls.Add($btnApply)

# --- State ---
$script:MonitorEntries = @()
$script:LiveConfigs = @{}
$script:HostForms = @{}
$script:WebcamCacheDir = Join-Path $env:LOCALAPPDATA "CGWallpaper"
$script:ForceExit = $false

function Load-ImageSafely {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $null
    }
    try {
        $bytes = [System.IO.File]::ReadAllBytes($Path)
        $ms = New-Object System.IO.MemoryStream(,$bytes)
        $tmpImg = [System.Drawing.Image]::FromStream($ms)
        $bmp = New-Object System.Drawing.Bitmap($tmpImg)
        $tmpImg.Dispose()
        $ms.Dispose()
        return $bmp
    } catch {
        return $null
    }
}

function Set-MonitorImage {
    param($Entry, [string]$ImagePath, [switch]$FromLive)
    if (-not (Test-Path -LiteralPath $ImagePath)) { return }
    $img = Load-ImageSafely -Path $ImagePath
    if ($null -eq $img) {
        $lblStatus.ForeColor = $accentRed
        $lblStatus.Text = "Format d'image non supporté."
        return
    }
    if (-not $FromLive) {
        if ($script:LiveConfigs.ContainsKey($Entry.MonitorId)) {
            $script:LiveConfigs.Remove($Entry.MonitorId)
        }
        $Entry.TopLabel.Text = $Entry.BaseTitle
        $Entry.TopLabel.ForeColor = $fgText
    }
    $oldImg = $Entry.PictureBox.Image
    $Entry.ImagePath = $ImagePath
    $Entry.PictureBox.Image = $img
    if ($null -ne $oldImg) {
        $oldImg.Dispose()
    }
    $fileName = [System.IO.Path]::GetFileName($ImagePath)
    if (-not $FromLive) {
        $Entry.FileLabel.Text = $fileName
        $Entry.Panel.BackColor = $bgCard
        $lblStatus.ForeColor = $fgMuted
        $lblStatus.Text = "Image sélectionnée : $fileName"
    }
}

function Show-WebcamDialog {
    param($Entry)
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = "Webcam Live - $($Entry.BaseTitle)"
    $dlg.Size = [System.Drawing.Size]::new(480, 230)
    $dlg.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
    $dlg.MaximizeBox = $false
    $dlg.MinimizeBox = $false
    $dlg.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterParent
    $dlg.BackColor = $bgForm
    $dlg.ForeColor = $fgText
    $dlg.Font = $fontNormal

    $lblUrl = New-Object System.Windows.Forms.Label
    $lblUrl.Text = "URL de la webcam (image JPG/PNG ou flux MJPEG) :"
    $lblUrl.Location = [System.Drawing.Point]::new(16, 16)
    $lblUrl.Size = [System.Drawing.Size]::new(430, 20)
    $dlg.Controls.Add($lblUrl)

    $txtUrl = New-Object System.Windows.Forms.TextBox
    $txtUrl.Location = [System.Drawing.Point]::new(16, 40)
    $txtUrl.Size = [System.Drawing.Size]::new(432, 26)
    $txtUrl.BackColor = $bgCard
    $txtUrl.ForeColor = $fgText
    $txtUrl.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
    if ($script:LiveConfigs.ContainsKey($Entry.MonitorId)) {
        $txtUrl.Text = $script:LiveConfigs[$Entry.MonitorId].Url
    }
    $dlg.Controls.Add($txtUrl)

    $lblInt = New-Object System.Windows.Forms.Label
    $lblInt.Text = "Rafraîchissement :"
    $lblInt.Location = [System.Drawing.Point]::new(16, 80)
    $lblInt.Size = [System.Drawing.Size]::new(130, 22)
    $lblInt.ForeColor = $fgMuted
    $dlg.Controls.Add($lblInt)

    $cmbInt = New-Object System.Windows.Forms.ComboBox
    $cmbInt.DropDownStyle = [System.Windows.Forms.ComboBoxStyle]::DropDownList
    $cmbInt.Location = [System.Drawing.Point]::new(150, 77)
    $cmbInt.Size = [System.Drawing.Size]::new(140, 26)
    $cmbInt.BackColor = $bgCard
    $cmbInt.ForeColor = $fgText
    $cmbInt.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
    $intervals = @(
        @{ Label = "5 secondes";  Sec = 5 },
        @{ Label = "10 secondes"; Sec = 10 },
        @{ Label = "15 secondes"; Sec = 15 },
        @{ Label = "30 secondes"; Sec = 30 },
        @{ Label = "1 minute";    Sec = 60 },
        @{ Label = "5 minutes";   Sec = 300 }
    )
    foreach ($it in $intervals) { [void]$cmbInt.Items.Add($it.Label) }
    $cmbInt.SelectedIndex = 1
    if ($script:LiveConfigs.ContainsKey($Entry.MonitorId)) {
        $curSec = $script:LiveConfigs[$Entry.MonitorId].IntervalSec
        for ($i = 0; $i -lt $intervals.Count; $i++) {
            if ($intervals[$i].Sec -eq $curSec) { $cmbInt.SelectedIndex = $i; break }
        }
    }
    $dlg.Controls.Add($cmbInt)

    $lblHint = New-Object System.Windows.Forms.Label
    $lblHint.Text = "Astuce : clic droit sur l'image d'une webcam web -> Copier l'adresse de l'image."
    $lblHint.Location = [System.Drawing.Point]::new(16, 114)
    $lblHint.Size = [System.Drawing.Size]::new(432, 20)
    $lblHint.Font = $fontSmall
    $lblHint.ForeColor = $fgMuted
    $dlg.Controls.Add($lblHint)

    $btnDisable = New-Object System.Windows.Forms.Button
    $btnDisable.Text = "Désactiver Live"
    $btnDisable.Location = [System.Drawing.Point]::new(16, 148)
    $btnDisable.Size = [System.Drawing.Size]::new(120, 32)
    $btnDisable.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
    $btnDisable.FlatAppearance.BorderColor = $borderCard
    $btnDisable.BackColor = $bgCard
    $btnDisable.ForeColor = $accentRed
    $btnDisable.Visible = $script:LiveConfigs.ContainsKey($Entry.MonitorId)
    $dlg.Controls.Add($btnDisable)

    $btnCancel = New-Object System.Windows.Forms.Button
    $btnCancel.Text = "Annuler"
    $btnCancel.Location = [System.Drawing.Point]::new(256, 148)
    $btnCancel.Size = [System.Drawing.Size]::new(90, 32)
    $btnCancel.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
    $btnCancel.FlatAppearance.BorderColor = $borderCard
    $btnCancel.BackColor = $bgCard
    $btnCancel.ForeColor = $fgText
    $dlg.Controls.Add($btnCancel)

    $btnOk = New-Object System.Windows.Forms.Button
    $btnOk.Text = "Activer"
    $btnOk.Font = $fontBold
    $btnOk.Location = [System.Drawing.Point]::new(354, 148)
    $btnOk.Size = [System.Drawing.Size]::new(94, 32)
    $btnOk.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
    $btnOk.FlatAppearance.BorderSize = 0
    $btnOk.BackColor = $accentBlue
    $btnOk.ForeColor = [System.Drawing.Color]::White
    $dlg.Controls.Add($btnOk)

    $btnCancel.Add_Click({ $dlg.Close() })
    $btnDisable.Add_Click({
        if ($script:LiveConfigs.ContainsKey($Entry.MonitorId)) {
            $script:LiveConfigs.Remove($Entry.MonitorId)
        }
        $Entry.TopLabel.Text = $Entry.BaseTitle
        $Entry.TopLabel.ForeColor = $fgText
        $Entry.FileLabel.Text = if ([string]::IsNullOrWhiteSpace($Entry.ImagePath)) { "Cliquer ou glisser une image" } else { [System.IO.Path]::GetFileName($Entry.ImagePath) }
        $lblStatus.ForeColor = $fgMuted
        $lblStatus.Text = "Webcam Live désactivée."
        $dlg.Close()
    })
    $btnOk.Add_Click({
        $u = $txtUrl.Text.Trim()
        if ($u -notmatch '^https?://') {
            [System.Windows.Forms.MessageBox]::Show($dlg, "Veuillez entrer une URL valide commençant par http:// ou https://", "URL invalide", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null
            return
        }
        $sec = $intervals[$cmbInt.SelectedIndex].Sec
        $script:LiveConfigs[$Entry.MonitorId] = @{
            Url         = $u
            IntervalSec = $sec
            LastFetch   = [DateTime]::MinValue
            Toggle      = $false
            Index       = $Entry.Index
        }
        $Entry.TopLabel.Text = "$($Entry.BaseTitle) • LIVE (${sec}s)"
        $Entry.TopLabel.ForeColor = $accentGreen
        $Entry.FileLabel.Text = "Connexion webcam..."
        $lblStatus.ForeColor = $fgMuted
        $lblStatus.Text = "Téléchargement de la webcam..."
        $dlg.Close()
    })

    $dlg.AcceptButton = $btnOk
    $dlg.CancelButton = $btnCancel
    [void]$dlg.ShowDialog($form)
    $dlg.Dispose()
}

function Browse-MonitorImage {
    param($Entry)
    $ofd = New-Object System.Windows.Forms.OpenFileDialog
    $ofd.Title = "Choisir un fond d'écran pour $($Entry.Title)"
    $ofd.Filter = "Images (*.jpg;*.jpeg;*.png;*.bmp;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.webp|Tous les fichiers (*.*)|*.*"
    if (-not [string]::IsNullOrWhiteSpace($Entry.ImagePath) -and (Test-Path -LiteralPath $Entry.ImagePath)) {
        $ofd.InitialDirectory = [System.IO.Path]::GetDirectoryName($Entry.ImagePath)
    } else {
        $ofd.InitialDirectory = [Environment]::GetFolderPath("MyPictures")
    }
    if ($ofd.ShowDialog($form) -eq [System.Windows.Forms.DialogResult]::OK) {
        Set-MonitorImage -Entry $Entry -ImagePath $ofd.FileName
    }
    $ofd.Dispose()
}

function Render-Monitors {
    foreach ($entry in $script:MonitorEntries) {
        if ($null -ne $entry.PictureBox.Image) {
            $oldImg = $entry.PictureBox.Image
            $entry.PictureBox.Image = $null
            $oldImg.Dispose()
        }
    }
    $canvas.Controls.Clear()
    $script:MonitorEntries = @()

    $count = [WallpaperHelper]::GetMonitorCount()

    $rawMonitors = @()
    for ($i = [uint32]0; $i -lt $count; $i++) {
        $monId = [WallpaperHelper]::GetMonitorId($i)
        try {
            $rect = [WallpaperHelper]::GetMonitorRect($monId)
        } catch {
            continue
        }
        $w = $rect.Right - $rect.Left
        $h = $rect.Bottom - $rect.Top
        if ($w -le 0 -or $h -le 0) { continue }

        $curWall = ""
        try { $curWall = [WallpaperHelper]::GetWallpaper($monId) } catch {}

        $rawMonitors += [PSCustomObject]@{
            Index     = $i + 1
            MonitorId = $monId
            Left      = $rect.Left
            Top       = $rect.Top
            Width     = $w
            Height    = $h
            IsPrimary = ($rect.Left -eq 0 -and $rect.Top -eq 0)
            ImagePath = $curWall
        }
    }

    if ($rawMonitors.Count -eq 0) {
        $lblEmpty = New-Object System.Windows.Forms.Label
        $lblEmpty.Text = "Aucun écran actif détecté."
        $lblEmpty.ForeColor = $fgMuted
        $lblEmpty.Dock = [System.Windows.Forms.DockStyle]::Fill
        $lblEmpty.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
        $canvas.Controls.Add($lblEmpty)
        return
    }

    [double]$minX = $rawMonitors[0].Left
    [double]$minY = $rawMonitors[0].Top
    [double]$maxX = $rawMonitors[0].Left + $rawMonitors[0].Width
    [double]$maxY = $rawMonitors[0].Top + $rawMonitors[0].Height
    foreach ($rm in $rawMonitors) {
        if ($rm.Left -lt $minX) { $minX = $rm.Left }
        if ($rm.Top -lt $minY) { $minY = $rm.Top }
        if (($rm.Left + $rm.Width) -gt $maxX) { $maxX = $rm.Left + $rm.Width }
        if (($rm.Top + $rm.Height) -gt $maxY) { $maxY = $rm.Top + $rm.Height }
    }

    [double]$totalW = [Math]::Max(1.0, $maxX - $minX)
    [double]$totalH = [Math]::Max(1.0, $maxY - $minY)

    [int]$pad = 28
    [double]$availW = [Math]::Max(100.0, [double]$canvas.ClientSize.Width - 2 * $pad)
    [double]$availH = [Math]::Max(100.0, [double]$canvas.ClientSize.Height - 2 * $pad)
    [double]$scale = [Math]::Min($availW / $totalW, $availH / $totalH)

    [int]$offsetX = $pad + [int](($availW - $totalW * $scale) / 2.0)
    [int]$offsetY = $pad + [int](($availH - $totalH * $scale) / 2.0)

    foreach ($m in $rawMonitors) {
        [int]$px = $offsetX + [int](($m.Left - $minX) * $scale)
        [int]$py = $offsetY + [int](($m.Top - $minY) * $scale)
        [int]$pw = [Math]::Max(80, [int]($m.Width * $scale) - 6)
        [int]$ph = [Math]::Max(80, [int]($m.Height * $scale) - 6)

        $panel = New-Object System.Windows.Forms.Panel
        $panel.Location = [System.Drawing.Point]::new($px + 3, $py + 3)
        $panel.Size = [System.Drawing.Size]::new($pw, $ph)
        $panel.BackColor = $bgCard
        $panel.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
        $panel.AllowDrop = $true
        $panel.Cursor = [System.Windows.Forms.Cursors]::Hand

        $titleText = "Écran $($m.Index) ($($m.Width)×$($m.Height))"
        if ($m.IsPrimary) { $titleText += " • Principal" }
        $isLive = $script:LiveConfigs.ContainsKey($m.MonitorId)

        $topBar = New-Object System.Windows.Forms.Label
        if ($isLive) {
            $sec = $script:LiveConfigs[$m.MonitorId].IntervalSec
            $topBar.Text = "$titleText • LIVE (${sec}s)"
            $topBar.ForeColor = $accentGreen
        } else {
            $topBar.Text = $titleText
            $topBar.ForeColor = $fgText
        }
        $topBar.Font = $fontBold
        $topBar.BackColor = [System.Drawing.Color]::FromArgb(30, 30, 46)
        $topBar.Dock = [System.Windows.Forms.DockStyle]::Top
        $topBar.Height = 24
        $topBar.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
        $topBar.Cursor = [System.Windows.Forms.Cursors]::Hand

        $bottomContainer = New-Object System.Windows.Forms.Panel
        $bottomContainer.Dock = [System.Windows.Forms.DockStyle]::Bottom
        $bottomContainer.Height = 24
        $bottomContainer.BackColor = [System.Drawing.Color]::FromArgb(30, 30, 46)

        $btnCam = New-Object System.Windows.Forms.Button
        $btnCam.Text = "Webcam"
        $btnCam.Font = $fontSmall
        $btnCam.Dock = [System.Windows.Forms.DockStyle]::Right
        $btnCam.Width = 64
        $btnCam.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
        $btnCam.FlatAppearance.BorderSize = 0
        $btnCam.BackColor = [System.Drawing.Color]::FromArgb(49, 50, 68)
        $btnCam.ForeColor = $borderFocus
        $btnCam.Cursor = [System.Windows.Forms.Cursors]::Hand

        $bottomBar = New-Object System.Windows.Forms.Label
        if ($isLive) {
            $bottomBar.Text = "Webcam Live active"
        } else {
            $bottomBar.Text = if ([string]::IsNullOrWhiteSpace($m.ImagePath)) { "Cliquer ou glisser une image" } else { [System.IO.Path]::GetFileName($m.ImagePath) }
        }
        $bottomBar.Font = $fontSmall
        $bottomBar.ForeColor = $fgMuted
        $bottomBar.BackColor = [System.Drawing.Color]::FromArgb(30, 30, 46)
        $bottomBar.Dock = [System.Windows.Forms.DockStyle]::Fill
        $bottomBar.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
        $bottomBar.Cursor = [System.Windows.Forms.Cursors]::Hand

        $bottomContainer.Controls.Add($bottomBar)
        $bottomContainer.Controls.Add($btnCam)

        $pic = New-Object System.Windows.Forms.PictureBox
        $pic.Dock = [System.Windows.Forms.DockStyle]::Fill
        $pic.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
        $pic.BackColor = $bgCard
        $pic.Cursor = [System.Windows.Forms.Cursors]::Hand
        $pic.AllowDrop = $true

        if (-not [string]::IsNullOrWhiteSpace($m.ImagePath) -and (Test-Path -LiteralPath $m.ImagePath)) {
            $pic.Image = Load-ImageSafely -Path $m.ImagePath
        }

        $panel.Controls.Add($pic)
        $panel.Controls.Add($topBar)
        $panel.Controls.Add($bottomContainer)

        $entry = [PSCustomObject]@{
            Index      = $m.Index
            MonitorId  = $m.MonitorId
            BaseTitle  = $titleText
            Title      = $titleText
            ImagePath  = $m.ImagePath
            MonLeft    = $m.Left
            MonTop     = $m.Top
            MonWidth   = $m.Width
            MonHeight  = $m.Height
            Panel      = $panel
            PictureBox = $pic
            TopLabel   = $topBar
            FileLabel  = $bottomBar
        }
        $script:MonitorEntries += $entry

        $btnCam.Tag = $entry
        $btnCam.Add_Click({
            param($sender, $e)
            Show-WebcamDialog -Entry $sender.Tag
        })

        # Click handlers
        $clickHandler = {
            param($sender, $e)
            $target = $sender.Tag
            Browse-MonitorImage -Entry $target
        }
        foreach ($ctrl in @($panel, $pic, $topBar, $bottomBar)) {
            $ctrl.Tag = $entry
            $ctrl.Add_Click($clickHandler)
        }

        # Drag & Drop handlers
        $dragEnter = {
            param($sender, $e)
            if ($e.Data.GetDataPresent([System.Windows.Forms.DataFormats]::FileDrop)) {
                $e.Effect = [System.Windows.Forms.DragDropEffects]::Copy
                $sender.Tag.Panel.BackColor = $bgCardHover
            }
        }
        $dragLeave = {
            param($sender, $e)
            $sender.Tag.Panel.BackColor = $bgCard
        }
        $dragDrop = {
            param($sender, $e)
            $sender.Tag.Panel.BackColor = $bgCard
            $files = $e.Data.GetData([System.Windows.Forms.DataFormats]::FileDrop)
            if ($files -and $files.Length -gt 0) {
                Set-MonitorImage -Entry $sender.Tag -ImagePath $files[0]
            }
        }
        foreach ($ctrl in @($panel, $pic)) {
            $ctrl.Add_DragEnter($dragEnter)
            $ctrl.Add_DragLeave($dragLeave)
            $ctrl.Add_DragDrop($dragDrop)
        }

        $canvas.Controls.Add($panel)
    }
}

$btnRefresh.Add_Click({
    Render-Monitors
    $lblStatus.ForeColor = $fgMuted
    $lblStatus.Text = "Écrans actualisés."
})

function Apply-MonitorWallpaper {
    param($Entry, [string]$ImagePath, $FitMode)
    if ([string]::IsNullOrWhiteSpace($ImagePath) -or -not (Test-Path -LiteralPath $ImagePath)) { return $false }

    # Try official API first (in case it's allowed)
    try {
        [WallpaperHelper]::SetPosition($FitMode)
        [WallpaperHelper]::SetWallpaper($Entry.MonitorId, $ImagePath)
    } catch {}

    # Render directly in the desktop layer behind icons (works even when Windows is unactivated or policy-locked)
    $img = Load-ImageSafely -Path $ImagePath
    if ($null -eq $img) { return $false }

    if (-not $script:HostForms.ContainsKey($Entry.MonitorId)) {
        $hf = New-Object WallpaperHostForm
        $hf.Size = [System.Drawing.Size]::new($Entry.MonWidth, $Entry.MonHeight)
        $hf.Location = [System.Drawing.Point]::new($Entry.MonLeft, $Entry.MonTop)
        $hf.WallpaperImage = $img
        $hf.FitMode = $FitMode
        $hf.Show()
        [DesktopLayerHelper]::AttachToDesktop($hf.Handle, $Entry.MonLeft, $Entry.MonTop, $Entry.MonWidth, $Entry.MonHeight)
        $script:HostForms[$Entry.MonitorId] = $hf
    } else {
        $hf = $script:HostForms[$Entry.MonitorId]
        $old = $hf.WallpaperImage
        $hf.WallpaperImage = $img
        $hf.FitMode = $FitMode
        if ($null -ne $old) { $old.Dispose() }
        [DesktopLayerHelper]::AttachToDesktop($hf.Handle, $Entry.MonLeft, $Entry.MonTop, $Entry.MonWidth, $Entry.MonHeight)
        $hf.Invalidate()
    }
    return $true
}

$btnApply.Add_Click({
    try {
        $selectedMode = $modeItems[$cmbMode.SelectedIndex].Value
        $appliedCount = 0
        foreach ($entry in $script:MonitorEntries) {
            if (Apply-MonitorWallpaper -Entry $entry -ImagePath $entry.ImagePath -FitMode $selectedMode) {
                $appliedCount++
            }
        }
        $lblStatus.ForeColor = $accentGreen
        $lblStatus.Text = "Fonds d'écran appliqués ($appliedCount écran(s)) !"
    } catch {
        $lblStatus.ForeColor = $accentRed
        $lblStatus.Text = "Erreur : $($_.Exception.Message)"
    }
})

# --- System Tray & Live Webcam Timer ---
$trayMenu = New-Object System.Windows.Forms.ContextMenuStrip
$miOpen = $trayMenu.Items.Add("Ouvrir")
$miExit = $trayMenu.Items.Add("Quitter")

$notifyIcon = New-Object System.Windows.Forms.NotifyIcon
$notifyIcon.Icon = [System.Drawing.SystemIcons]::Application
$notifyIcon.Text = "CGWallpaper - Webcam Live actif"
$notifyIcon.ContextMenuStrip = $trayMenu
$notifyIcon.Visible = $false

$miOpen.Add_Click({
    $form.Show()
    $form.WindowState = [System.Windows.Forms.FormWindowState]::Normal
    $form.Activate()
    $notifyIcon.Visible = $false
})
$notifyIcon.Add_DoubleClick({
    $form.Show()
    $form.WindowState = [System.Windows.Forms.FormWindowState]::Normal
    $form.Activate()
    $notifyIcon.Visible = $false
})
$miExit.Add_Click({
    $script:ForceExit = $true
    $notifyIcon.Visible = $false
    $form.Close()
})

$form.Add_FormClosing({
    param($sender, $e)
    if (-not $script:ForceExit -and ($script:LiveConfigs.Count -gt 0 -or $script:HostForms.Count -gt 0)) {
        $e.Cancel = $true
        $form.Hide()
        $notifyIcon.Visible = $true
        $notifyIcon.ShowBalloonTip(2500, "CGWallpaper", "Vos fonds d'écran restent actifs en arrière-plan. Clic droit sur l'icône pour quitter.", [System.Windows.Forms.ToolTipIcon]::Info)
    } else {
        $liveTimer.Stop()
        foreach ($hf in $script:HostForms.Values) {
            if ($null -ne $hf.WallpaperImage) { $hf.WallpaperImage.Dispose() }
            $hf.Close()
            $hf.Dispose()
        }
        $script:HostForms.Clear()
        $notifyIcon.Dispose()
    }
})

$liveTimer = New-Object System.Windows.Forms.Timer
$liveTimer.Interval = 500
$liveTimer.Add_Tick({
    $now = [DateTime]::UtcNow
    foreach ($entry in $script:MonitorEntries) {
        if ($script:LiveConfigs.ContainsKey($entry.MonitorId)) {
            $cfg = $script:LiveConfigs[$entry.MonitorId]
            if (($now - $cfg.LastFetch).TotalSeconds -ge $cfg.IntervalSec) {
                $cfg.LastFetch = $now
                $cfg.Toggle = -not $cfg.Toggle
                $suffix = if ($cfg.Toggle) { "a" } else { "b" }
                $dest = Join-Path $script:WebcamCacheDir "cam_$($cfg.Index)_$suffix.jpg"
                [WebcamFetcher]::FetchAsync($entry.MonitorId, $cfg.Url, $dest)
            }
        }
    }

    $res = $null
    while ([WebcamFetcher]::TryDequeue([ref]$res)) {
        $targetEntry = $script:MonitorEntries | Where-Object { $_.MonitorId -eq $res.MonitorId } | Select-Object -First 1
        if ($null -eq $targetEntry) { continue }
        if ([string]::IsNullOrWhiteSpace($res.Error)) {
            Set-MonitorImage -Entry $targetEntry -ImagePath $res.FilePath -FromLive
            try {
                $selectedMode = $modeItems[$cmbMode.SelectedIndex].Value
                [void](Apply-MonitorWallpaper -Entry $targetEntry -ImagePath $res.FilePath -FitMode $selectedMode)
                $ts = (Get-Date).ToString("HH:mm:ss")
                $targetEntry.FileLabel.Text = "Live • $ts"
                $lblStatus.ForeColor = $accentGreen
                $lblStatus.Text = "Webcam mise à jour à $ts"
            } catch {
                $lblStatus.ForeColor = $accentRed
                $lblStatus.Text = "Erreur fond d'écran : $($_.Exception.Message)"
            }
        } else {
            $targetEntry.FileLabel.Text = "Erreur webcam"
            $lblStatus.ForeColor = $accentRed
            $lblStatus.Text = "Webcam : $($res.Error)"
        }
    }
})

$form.Add_Shown({
    Render-Monitors
    $liveTimer.Start()
})
$canvas.Add_Resize({
    if ($form.Visible) { Render-Monitors }
})

if ($TestOnly) {
    Render-Monitors
    Write-Host "Monitors detected: $($script:MonitorEntries.Count)"
    foreach ($e in $script:MonitorEntries) {
        Write-Host "  - $($e.Title) | Pos: $($e.Panel.Location) | Size: $($e.Panel.Size) | Current: $($e.ImagePath)"
    }
    $form.Dispose()
    $notifyIcon.Dispose()
    $liveTimer.Dispose()
} else {
    [void]$form.ShowDialog()
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CGWallpaper;

public class WallpaperHostForm : Form
{
    private MonitorInfoItem _monitor;
    private Image? _image;
    private WallpaperFitMode _fitMode = WallpaperFitMode.Fill;
    private double _offsetX = 0.5;
    private double _offsetY = 0.5;
    private WebView2? _webView;
    private bool _webReady;
    private string? _pendingUrl;
    private string? _webInjectJs;

    public WallpaperHostForm(MonitorInfoItem monitor)
    {
        _monitor = monitor;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    public void Attach()
    {
        if (!IsHandleCreated) CreateHandle();
        DesktopLayer.AttachToDesktop(Handle, _monitor.Left, _monitor.Top, _monitor.Width, _monitor.Height);
        DesktopLayer.EnsureZOrder(Handle);
    }

    public void UpdateMonitor(MonitorInfoItem monitor)
    {
        _monitor = monitor;
        Attach();
    }

    public void ShowImage(string path, WallpaperFitMode fitMode, double offsetX = 0.5, double offsetY = 0.5, double softness = 0.0)
    {
        if (!File.Exists(path)) return;
        HideWebView();

        byte[] bytes = File.ReadAllBytes(path);
        using var ms = new MemoryStream(bytes);
        using var tmp = Image.FromStream(ms);
        var bmp = new Bitmap(tmp);

        if (softness > 0.05)
        {
            ApplyFastSoften(bmp, softness);
        }

        var old = _image;
        _image = bmp;
        _fitMode = fitMode;
        _offsetX = Math.Clamp(offsetX, 0.0, 1.0);
        _offsetY = Math.Clamp(offsetY, 0.0, 1.0);
        old?.Dispose();
        Invalidate();
    }

    private static unsafe void ApplyFastSoften(Bitmap bmp, double softness)
    {
        int passes = softness >= 1.8 ? 2 : 1;
        int w = bmp.Width;
        int h = bmp.Height;
        if (w < 3 || h < 3) return;

        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            int total = stride * h;
            byte[] src = new byte[total];
            byte[] dst = new byte[total];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, src, 0, total);

            double blend = Math.Clamp(softness / 3.0, 0.15, 1.0);
            for (int p = 0; p < passes; p++)
            {
                Buffer.BlockCopy(src, 0, dst, 0, total);
                for (int y = 1; y < h - 1; y++)
                {
                    int row = y * stride;
                    for (int x = 1; x < w - 1; x++)
                    {
                        int idx = row + x * 4;
                        for (int c = 0; c < 3; c++)
                        {
                            int avg = (src[idx - stride - 4 + c] + src[idx - stride + c] + src[idx - stride + 4 + c] +
                                       src[idx - 4 + c]          + src[idx + c]          + src[idx + 4 + c] +
                                       src[idx + stride - 4 + c] + src[idx + stride + c] + src[idx + stride + 4 + c]) / 9;
                            dst[idx + c] = (byte)(src[idx + c] * (1.0 - blend) + avg * blend);
                        }
                    }
                }
                Buffer.BlockCopy(dst, 0, src, 0, total);
            }
            System.Runtime.InteropServices.Marshal.Copy(src, 0, data.Scan0, total);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static string BuildCssFilter(double softness)
    {
        if (softness <= 0.05) return "none";
        string blur = softness.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        string contrast = (1.0 - softness * 0.025).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        return $"blur({blur}px) contrast({contrast})";
    }

    public void ShowVideo(string path, WallpaperFitMode fitMode, bool muteAudio, double offsetX = 0.5, double offsetY = 0.5, double softness = 0.0)
    {
        if (!File.Exists(path)) return;
        _webInjectJs = null;
        string objectFit = MapObjectFit(fitMode);
        string ox = (Math.Clamp(offsetX, 0.0, 1.0) * 100.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        string oy = (Math.Clamp(offsetY, 0.0, 1.0) * 100.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        string cssFilter = BuildCssFilter(softness);
        string fileUrl = new Uri(path).AbsoluteUri;
        string mutedAttr = muteAudio ? "muted" : "";
        string html = $@"<!DOCTYPE html>
<html><body style='margin:0;padding:0;overflow:hidden;background:#000;'>
<video autoplay loop {mutedAttr} playsinline style='width:100vw;height:100vh;object-fit:{objectFit};object-position:{ox}% {oy}%;filter:{cssFilter};display:block;'>
  <source src='{fileUrl}'/>
</video></body></html>";
        ShowWebContent(html, isHtml: true, muteAudio: muteAudio);
    }

    public void ShowWebPage(string url, bool muteAudio, WallpaperFitMode fitMode = WallpaperFitMode.Fill, double offsetX = 0.5, double offsetY = 0.5, double softness = 0.0)
    {
        string pathOnly = url.Split('?')[0].ToLowerInvariant();
        if (pathOnly.EndsWith(".m3u8"))
        {
            ShowHlsStream(url, fitMode, muteAudio, offsetX, offsetY, softness);
            return;
        }
        if (pathOnly.EndsWith(".mp4") || pathOnly.EndsWith(".webm"))
        {
            ShowVideoUrl(url, fitMode, muteAudio, offsetX, offsetY, softness);
            return;
        }
        _webInjectJs = BuildVideoPageCssJs(fitMode, offsetX, offsetY, softness);
        ShowWebContent(url, isHtml: false, muteAudio: muteAudio);
    }

    private static string BuildVideoPageCssJs(WallpaperFitMode fitMode, double offsetX, double offsetY, double softness)
    {
        string objectFit = MapObjectFit(fitMode);
        string ox = (Math.Clamp(offsetX, 0.0, 1.0) * 100.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        string oy = (Math.Clamp(offsetY, 0.0, 1.0) * 100.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        string cssFilter = BuildCssFilter(softness);
        return $@"(() => {{
  const apply = () => {{
    const v = document.querySelector('video');
    if (!v) return false;
    v.muted = true;
    v.play().catch(() => {{}});
    v.style.setProperty('position', 'fixed', 'important');
    v.style.setProperty('top', '0', 'important');
    v.style.setProperty('left', '0', 'important');
    v.style.setProperty('width', '100vw', 'important');
    v.style.setProperty('height', '100vh', 'important');
    v.style.setProperty('max-width', 'none', 'important');
    v.style.setProperty('max-height', 'none', 'important');
    v.style.setProperty('object-fit', '{objectFit}', 'important');
    v.style.setProperty('object-position', '{ox}% {oy}%', 'important');
    v.style.setProperty('filter', '{cssFilter}', 'important');
    v.style.setProperty('z-index', '2147483647', 'important');
    v.style.setProperty('background', '#000', 'important');
    let p = v.parentElement;
    while (p && p !== document.documentElement) {{
      p.style.setProperty('transform', 'none', 'important');
      p.style.setProperty('filter', 'none', 'important');
      p.style.setProperty('perspective', 'none', 'important');
      p.style.setProperty('contain', 'none', 'important');
      p.style.setProperty('overflow', 'visible', 'important');
      p = p.parentElement;
    }}
    return true;
  }};
  if (!apply()) {{
    const t = setInterval(() => {{ if (apply()) clearInterval(t); }}, 300);
    setTimeout(() => clearInterval(t), 15000);
  }}
}})();";
    }

    private void ShowVideoUrl(string url, WallpaperFitMode fitMode, bool muteAudio, double offsetX, double offsetY, double softness)
    {
        _webInjectJs = null;
        string objectFit = MapObjectFit(fitMode);
        string ox = (Math.Clamp(offsetX, 0.0, 1.0) * 100.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        string oy = (Math.Clamp(offsetY, 0.0, 1.0) * 100.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        string cssFilter = BuildCssFilter(softness);
        string mutedAttr = muteAudio ? "muted" : "";
        string safeUrl = System.Net.WebUtility.HtmlEncode(url);
        string html = $@"<!DOCTYPE html>
<html><body style='margin:0;padding:0;overflow:hidden;background:#000;'>
<video autoplay loop {mutedAttr} playsinline style='width:100vw;height:100vh;object-fit:{objectFit};object-position:{ox}% {oy}%;filter:{cssFilter};display:block;'>
  <source src='{safeUrl}'/>
</video></body></html>";
        ShowWebContent(html, isHtml: true, muteAudio: muteAudio);
    }

    private void ShowHlsStream(string m3u8Url, WallpaperFitMode fitMode, bool muteAudio, double offsetX, double offsetY, double softness)
    {
        _webInjectJs = null;
        string objectFit = MapObjectFit(fitMode);
        string ox = (Math.Clamp(offsetX, 0.0, 1.0) * 100.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        string oy = (Math.Clamp(offsetY, 0.0, 1.0) * 100.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        string cssFilter = BuildCssFilter(softness);
        string mutedAttr = muteAudio ? "muted" : "";
        string jsUrl = System.Text.Json.JsonSerializer.Serialize(m3u8Url);
        string html = $@"<!DOCTYPE html>
<html><body style='margin:0;padding:0;overflow:hidden;background:#000;'>
<video id='v' autoplay {mutedAttr} playsinline style='width:100vw;height:100vh;object-fit:{objectFit};object-position:{ox}% {oy}%;filter:{cssFilter};display:block;'></video>
<script src='https://cdn.jsdelivr.net/npm/hls.js@1'></script>
<script>
  const v = document.getElementById('v');
  const src = {jsUrl};
  if (typeof Hls !== 'undefined' && Hls.isSupported()) {{
    const hls = new Hls({{ lowLatencyMode: true, backBufferLength: 30 }});
    hls.loadSource(src);
    hls.attachMedia(v);
    hls.on(Hls.Events.MANIFEST_PARSED, () => v.play().catch(() => {{}}));
    hls.on(Hls.Events.ERROR, (e, data) => {{
      if (data.fatal) {{
        if (data.type === Hls.ErrorTypes.NETWORK_ERROR) setTimeout(() => hls.startLoad(), 2000);
        else if (data.type === Hls.ErrorTypes.MEDIA_ERROR) hls.recoverMediaError();
      }}
    }});
  }} else if (v.canPlayType('application/vnd.apple.mpegurl')) {{
    v.src = src;
    v.play().catch(() => {{}});
  }}
</script></body></html>";
        ShowWebContent(html, isHtml: true, muteAudio: muteAudio);
    }

    private static string MapObjectFit(WallpaperFitMode fitMode) => fitMode switch
    {
        WallpaperFitMode.Fit => "contain",
        WallpaperFitMode.Stretch => "fill",
        WallpaperFitMode.Center => "none",
        _ => "cover"
    };

    private async void ShowWebContent(string target, bool isHtml, bool muteAudio)
    {
        var old = _image;
        _image = null;
        old?.Dispose();
        Invalidate();

        if (_webView == null)
        {
            _webView = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.Black
            };
            Controls.Add(_webView);
        }
        _webView.Visible = true;
        _webView.BringToFront();

        if (!_webReady)
        {
            _pendingUrl = isHtml ? "HTML:" + target : target;
            try
            {
                string userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CGWallpaper", "WebView2");
                var env = await CoreWebView2Environment.CreateAsync(null, userData);
                await _webView.EnsureCoreWebView2Async(env);
                _webView.CoreWebView2.IsMuted = muteAudio;
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _webView.CoreWebView2.NavigationCompleted += async (_, _) =>
                {
                    if (!string.IsNullOrEmpty(_webInjectJs) && _webView?.CoreWebView2 != null)
                    {
                        try { await _webView.CoreWebView2.ExecuteScriptAsync(_webInjectJs); } catch { }
                    }
                };
                _webReady = true;
                if (_pendingUrl.StartsWith("HTML:"))
                    _webView.CoreWebView2.NavigateToString(_pendingUrl.Substring(5));
                else
                    _webView.CoreWebView2.Navigate(_pendingUrl);
            }
            catch { }
        }
        else if (_webView.CoreWebView2 != null)
        {
            _webView.CoreWebView2.IsMuted = muteAudio;
            if (isHtml)
                _webView.CoreWebView2.NavigateToString(target);
            else
                _webView.CoreWebView2.Navigate(target);
        }
    }

    private void HideWebView()
    {
        if (_webView != null)
        {
            _webView.Visible = false;
            if (_webReady && _webView.CoreWebView2 != null)
                _webView.CoreWebView2.Navigate("about:blank");
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_image == null) return;

        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        int cw = ClientSize.Width;
        int ch = ClientSize.Height;
        int iw = _image.Width;
        int ih = _image.Height;
        if (cw <= 0 || ch <= 0 || iw <= 0 || ih <= 0) return;

        if (_fitMode == WallpaperFitMode.Stretch)
        {
            g.DrawImage(_image, 0, 0, cw, ch);
        }
        else if (_fitMode == WallpaperFitMode.Center)
        {
            int dx = iw > cw ? -(int)((iw - cw) * _offsetX) : (cw - iw) / 2;
            int dy = ih > ch ? -(int)((ih - ch) * _offsetY) : (ch - ih) / 2;
            g.DrawImage(_image, dx, dy, iw, ih);
        }
        else if (_fitMode == WallpaperFitMode.Fit)
        {
            double scale = Math.Min((double)cw / iw, (double)ch / ih);
            int dw = (int)(iw * scale);
            int dh = (int)(ih * scale);
            g.DrawImage(_image, (cw - dw) / 2, (ch - dh) / 2, dw, dh);
        }
        else
        {
            double scale = Math.Max((double)cw / iw, (double)ch / ih);
            int dw = (int)(iw * scale);
            int dh = (int)(ih * scale);
            int dx = dw > cw ? -(int)((dw - cw) * _offsetX) : (cw - dw) / 2;
            int dy = dh > ch ? -(int)((dh - ch) * _offsetY) : (ch - dh) / 2;
            g.DrawImage(_image, dx, dy, dw, dh);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _image?.Dispose();
            _image = null;
            _webView?.Dispose();
            _webView = null;
        }
        base.Dispose(disposing);
    }
}

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

    public void ShowImage(string path, WallpaperFitMode fitMode, double offsetX = 0.5, double offsetY = 0.5)
    {
        if (!File.Exists(path)) return;
        HideWebView();

        byte[] bytes = File.ReadAllBytes(path);
        using var ms = new MemoryStream(bytes);
        using var tmp = Image.FromStream(ms);
        var bmp = new Bitmap(tmp);

        var old = _image;
        _image = bmp;
        _fitMode = fitMode;
        _offsetX = Math.Clamp(offsetX, 0.0, 1.0);
        _offsetY = Math.Clamp(offsetY, 0.0, 1.0);
        old?.Dispose();
        Invalidate();
    }

    public void ShowVideo(string path, WallpaperFitMode fitMode, bool muteAudio, double offsetX = 0.5, double offsetY = 0.5)
    {
        if (!File.Exists(path)) return;
        string objectFit = MapObjectFit(fitMode);
        string ox = (Math.Clamp(offsetX, 0.0, 1.0) * 100.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        string oy = (Math.Clamp(offsetY, 0.0, 1.0) * 100.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        string fileUrl = new Uri(path).AbsoluteUri;
        string mutedAttr = muteAudio ? "muted" : "";
        string html = $@"<!DOCTYPE html>
<html><body style='margin:0;padding:0;overflow:hidden;background:#000;'>
<video autoplay loop {mutedAttr} playsinline style='width:100vw;height:100vh;object-fit:{objectFit};object-position:{ox}% {oy}%;display:block;'>
  <source src='{fileUrl}'/>
</video></body></html>";
        ShowWebContent(html, isHtml: true, muteAudio: muteAudio);
    }

    public void ShowWebPage(string url, bool muteAudio, WallpaperFitMode fitMode = WallpaperFitMode.Fill, double offsetX = 0.5, double offsetY = 0.5)
    {
        string pathOnly = url.Split('?')[0].ToLowerInvariant();
        if (pathOnly.EndsWith(".m3u8"))
        {
            ShowHlsStream(url, fitMode, muteAudio, offsetX, offsetY);
            return;
        }
        if (pathOnly.EndsWith(".mp4") || pathOnly.EndsWith(".webm"))
        {
            ShowVideoUrl(url, fitMode, muteAudio, offsetX, offsetY);
            return;
        }
        ShowWebContent(url, isHtml: false, muteAudio: muteAudio);
    }

    private void ShowVideoUrl(string url, WallpaperFitMode fitMode, bool muteAudio, double offsetX, double offsetY)
    {
        string objectFit = MapObjectFit(fitMode);
        string ox = (Math.Clamp(offsetX, 0.0, 1.0) * 100.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        string oy = (Math.Clamp(offsetY, 0.0, 1.0) * 100.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        string mutedAttr = muteAudio ? "muted" : "";
        string safeUrl = System.Net.WebUtility.HtmlEncode(url);
        string html = $@"<!DOCTYPE html>
<html><body style='margin:0;padding:0;overflow:hidden;background:#000;'>
<video autoplay loop {mutedAttr} playsinline style='width:100vw;height:100vh;object-fit:{objectFit};object-position:{ox}% {oy}%;display:block;'>
  <source src='{safeUrl}'/>
</video></body></html>";
        ShowWebContent(html, isHtml: true, muteAudio: muteAudio);
    }

    private void ShowHlsStream(string m3u8Url, WallpaperFitMode fitMode, bool muteAudio, double offsetX, double offsetY)
    {
        string objectFit = MapObjectFit(fitMode);
        string ox = (Math.Clamp(offsetX, 0.0, 1.0) * 100.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        string oy = (Math.Clamp(offsetY, 0.0, 1.0) * 100.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        string mutedAttr = muteAudio ? "muted" : "";
        string jsUrl = System.Text.Json.JsonSerializer.Serialize(m3u8Url);
        string html = $@"<!DOCTYPE html>
<html><body style='margin:0;padding:0;overflow:hidden;background:#000;'>
<video id='v' autoplay {mutedAttr} playsinline style='width:100vw;height:100vh;object-fit:{objectFit};object-position:{ox}% {oy}%;display:block;'></video>
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

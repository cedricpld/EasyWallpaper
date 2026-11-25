using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace CGWallpaper;

public partial class MainWindow : Window
{
    private static readonly string[] VideoExtensions = [".mp4", ".webm", ".mkv", ".mov", ".avi", ".wmv"];
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif"];

    private readonly Dictionary<string, MonitorWallpaperConfig> _configs = new();
    private readonly Dictionary<string, WallpaperHostForm> _hosts = new();
    private readonly Dictionary<string, string> _cachedFrames = new();
    private readonly Dictionary<string, DateTime> _lastFetch = new();
    private readonly Dictionary<string, bool> _toggleFile = new();
    private readonly HashSet<string> _inFlight = new();

    private readonly DispatcherTimer _webcamTimer;
    private readonly System.Windows.Forms.NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.ToolStripMenuItem _miStartup;
    private List<MonitorInfoItem> _monitors = new();
    private bool _forceExit;

    public MainWindow(bool startMinimized = false)
    {
        InitializeComponent();

        System.Drawing.Icon? appIco = null;
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            string? resName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
            if (resName != null)
            {
                using var s = asm.GetManifestResourceStream(resName);
                if (s != null) appIco = new System.Drawing.Icon(s);
            }
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/assets/app.ico", UriKind.Absolute));
        }
        catch { }

        _miStartup = new System.Windows.Forms.ToolStripMenuItem("Lancer au démarrage")
        {
            CheckOnClick = true,
            Checked = ConfigService.IsStartupEnabled()
        };
        _miStartup.CheckedChanged += (_, _) => ConfigService.SetStartupEnabled(_miStartup.Checked);

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Ouvrir", null, (_, _) => RestoreFromTray());
        menu.Items.Add(_miStartup);
        menu.Items.Add("Redémarrer", null, (_, _) => RestartApp());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => ExitApp());

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = appIco ?? System.Drawing.SystemIcons.Application,
            Text = "EasyWallpaper",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        _webcamTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _webcamTimer.Tick += WebcamTimer_Tick;
        _webcamTimer.Start();

        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;

        LoadSavedConfig();
        RefreshMonitors();

        if (_configs.Values.Any(c => c.Type != WallpaperType.None))
        {
            ApplyAllWallpapers();
        }
    }

    private void LoadSavedConfig()
    {
        var saved = ConfigService.Load();
        foreach (var m in saved.Monitors)
        {
            _configs[m.MonitorId] = m;
        }
    }

    private void SaveCurrentConfig()
    {
        var cfg = new AppConfig
        {
            Monitors = _configs.Values.ToList()
        };
        ConfigService.Save(cfg);
    }

    private void SetStatus(string text, bool isSuccess = false, bool isError = false)
    {
        TxtStatus.Text = text;
        if (isError)
        {
            StatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 113, 113));
            TxtStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(252, 165, 165));
        }
        else if (isSuccess)
        {
            StatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 250, 250));
            TxtStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 250, 250));
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(113, 113, 122));
            TxtStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(161, 161, 170));
        }
    }

    private void RefreshMonitors()
    {
        _monitors = DesktopLayer.GetMonitors();
        RenderMonitorCards();
    }

    private void RenderMonitorCards()
    {
        foreach (UIElement child in MonitorCanvas.Children)
        {
            if (child is Border b && b.Child is Grid g)
            {
                foreach (UIElement gc in g.Children)
                {
                    if (gc is Grid pg)
                    {
                        foreach (UIElement p in pg.Children)
                        {
                            if (p is Microsoft.Web.WebView2.Wpf.WebView2 oldWv) oldWv.Dispose();
                        }
                    }
                }
            }
        }
        MonitorCanvas.Children.Clear();
        if (_monitors.Count == 0 || MonitorCanvas.ActualWidth < 50 || MonitorCanvas.ActualHeight < 50) return;

        double minX = _monitors.Min(m => m.Left);
        double minY = _monitors.Min(m => m.Top);
        double maxX = _monitors.Max(m => m.Left + m.Width);
        double maxY = _monitors.Max(m => m.Top + m.Height);

        double totalW = Math.Max(1, maxX - minX);
        double totalH = Math.Max(1, maxY - minY);

        double pad = 28;
        double chromeH = 66; // 28 topBar + 36 bottomBar + 2 border
        double availW = Math.Max(100, MonitorCanvas.ActualWidth - 2 * pad);
        double availH = Math.Max(100, MonitorCanvas.ActualHeight - 2 * pad - chromeH);
        double scale = Math.Min(availW / totalW, availH / totalH);

        double offsetX = pad + (availW - totalW * scale) / 2.0;
        double offsetY = pad + (availH - totalH * scale) / 2.0;

        foreach (var mon in _monitors)
        {
            double px = offsetX + (mon.Left - minX) * scale;
            double py = offsetY + (mon.Top - minY) * scale;
            double previewW = Math.Max(140, mon.Width * scale - 10);
            double previewH = previewW * ((double)mon.Height / mon.Width);
            double pw = previewW + 2;
            double ph = previewH + chromeH;

            if (!_configs.TryGetValue(mon.MonitorId, out var cfg))
            {
                cfg = new MonitorWallpaperConfig { MonitorId = mon.MonitorId, FitMode = WallpaperFitMode.Fill };
                _configs[mon.MonitorId] = cfg;
            }

            var card = new Border
            {
                Width = pw,
                Height = ph,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 24, 27)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 46, 50)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                AllowDrop = true,
                Cursor = System.Windows.Input.Cursors.Hand,
                ClipToBounds = true
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });

            // Top bar
            bool isLive = cfg.Type == WallpaperType.WebcamSnapshot || cfg.Type == WallpaperType.WebPage;
            var topBar = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 18, 20)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 39, 42)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new TextBlock
                {
                    Text = isLive ? $"{mon.Title} · LIVE" : mon.Title,
                    Foreground = new SolidColorBrush(isLive
                        ? System.Windows.Media.Color.FromRgb(250, 250, 250)
                        : System.Windows.Media.Color.FromRgb(212, 212, 216)),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11.5,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetRow(topBar, 0);

            // Accurate real-time preview area with exact monitor aspect ratio
            double cw = previewW;
            double ch = previewH;
            var previewGrid = new Grid
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(10, 10, 11)),
                ClipToBounds = true
            };
            bool hasPannableImage = false;
            string? imageToPreview = null;
            if (cfg.Type == WallpaperType.Image && File.Exists(cfg.Source))
                imageToPreview = cfg.Source;
            else if (cfg.Type == WallpaperType.WebcamSnapshot && _cachedFrames.TryGetValue(mon.MonitorId, out var cf) && File.Exists(cf))
                imageToPreview = cf;

            if (imageToPreview != null)
            {
                var bmp = LoadPreviewBitmap(imageToPreview);
                if (bmp != null)
                {
                    double iw = bmp.PixelWidth;
                    double ih = bmp.PixelHeight;
                    double dw = cw, dh = ch;
                    if (cfg.FitMode == WallpaperFitMode.Stretch)
                    {
                        dw = cw; dh = ch;
                    }
                    else if (cfg.FitMode == WallpaperFitMode.Center)
                    {
                        double ratio = cw / mon.Width;
                        dw = iw * ratio;
                        dh = ih * ratio;
                    }
                    else if (cfg.FitMode == WallpaperFitMode.Fit)
                    {
                        double sc = Math.Min(cw / iw, ch / ih);
                        dw = iw * sc;
                        dh = ih * sc;
                    }
                    else
                    {
                        double sc = Math.Max(cw / iw, ch / ih);
                        dw = iw * sc;
                        dh = ih * sc;
                    }

                    double slackX = Math.Max(0, dw - cw);
                    double slackY = Math.Max(0, dh - ch);
                    double dx = slackX > 0 ? -slackX * cfg.OffsetX : (cw - dw) / 2.0;
                    double dy = slackY > 0 ? -slackY * cfg.OffsetY : (ch - dh) / 2.0;

                    var previewCanvas = new Canvas { Width = cw, Height = ch, ClipToBounds = true };
                    var img = new System.Windows.Controls.Image
                    {
                        Source = bmp,
                        Width = dw,
                        Height = dh,
                        Stretch = Stretch.Fill
                    };
                    Canvas.SetLeft(img, dx);
                    Canvas.SetTop(img, dy);
                    previewCanvas.Children.Add(img);
                    previewGrid.Children.Add(previewCanvas);

                    hasPannableImage = slackX > 1 || slackY > 1;
                    if (hasPannableImage)
                        previewGrid.Cursor = System.Windows.Input.Cursors.SizeAll;

                    bool isDragging = false;
                    bool didMove = false;
                    System.Windows.Point dragStart = default;
                    double startOffX = cfg.OffsetX;
                    double startOffY = cfg.OffsetY;

                    previewGrid.MouseLeftButtonDown += (_, e) =>
                    {
                        isDragging = true;
                        didMove = false;
                        dragStart = e.GetPosition(previewGrid);
                        startOffX = cfg.OffsetX;
                        startOffY = cfg.OffsetY;
                        previewGrid.CaptureMouse();
                        e.Handled = true;
                    };
                    previewGrid.MouseMove += (_, e) =>
                    {
                        if (!isDragging) return;
                        var pos = e.GetPosition(previewGrid);
                        double deltaX = pos.X - dragStart.X;
                        double deltaY = pos.Y - dragStart.Y;
                        if (Math.Abs(deltaX) > 4 || Math.Abs(deltaY) > 4) didMove = true;
                        if (!didMove) return;

                        if (slackX > 1)
                            cfg.OffsetX = Math.Clamp(startOffX - deltaX / slackX, 0.0, 1.0);
                        if (slackY > 1)
                            cfg.OffsetY = Math.Clamp(startOffY - deltaY / slackY, 0.0, 1.0);

                        Canvas.SetLeft(img, slackX > 0 ? -slackX * cfg.OffsetX : (cw - dw) / 2.0);
                        Canvas.SetTop(img, slackY > 0 ? -slackY * cfg.OffsetY : (ch - dh) / 2.0);
                    };
                    previewGrid.MouseLeftButtonUp += (_, e) =>
                    {
                        if (!isDragging) return;
                        isDragging = false;
                        previewGrid.ReleaseMouseCapture();
                        if (!didMove) BrowseLocalFile(mon);
                        e.Handled = true;
                    };
                }
            }
            else if (cfg.Type == WallpaperType.Video || cfg.Type == WallpaperType.WebPage)
            {
                var wv = new Microsoft.Web.WebView2.Wpf.WebView2
                {
                    DefaultBackgroundColor = System.Drawing.Color.FromArgb(10, 10, 11)
                };
                previewGrid.Children.Add(wv);
                InitPreviewWebView(wv, mon, cfg);
            }
            else if (cfg.Type != WallpaperType.None)
            {
                string label = $"Webcam ({cfg.RefreshSeconds}s)\n{cfg.Source}";
                previewGrid.Children.Add(new TextBlock
                {
                    Text = label,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(228, 228, 231)),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    FontSize = 11.5
                });
            }
            else
            {
                previewGrid.Children.Add(new TextBlock
                {
                    Text = "Cliquer ou glisser\nune image / vidéo",
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(113, 113, 122)),
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    FontSize = 11.5
                });
            }
            Grid.SetRow(previewGrid, 1);

            // Bottom bar with independent per-monitor FitMode + URL/Live button
            var bottomBar = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 18, 20)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 39, 42)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(6, 3, 6, 3)
            };
            var bottomGrid = new Grid();
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string bottomText = cfg.Type == WallpaperType.None
                ? "Aucun média"
                : (cfg.Type == WallpaperType.Image || cfg.Type == WallpaperType.Video
                    ? Path.GetFileName(cfg.Source)
                    : cfg.Source);

            var lblFile = new TextBlock
            {
                Text = bottomText,
                ToolTip = bottomText,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(142, 142, 147)),
                FontSize = 11,
                Margin = new Thickness(2, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(lblFile, 0);

            var cmbFit = new System.Windows.Controls.ComboBox
            {
                Style = (Style)System.Windows.Application.Current.FindResource("MonoComboBox"),
                Width = 92,
                MinHeight = 26,
                Height = 26,
                FontSize = 11,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            cmbFit.Items.Add(new ComboBoxItem { Tag = WallpaperFitMode.Fill, Content = "Remplir" });
            cmbFit.Items.Add(new ComboBoxItem { Tag = WallpaperFitMode.Fit, Content = "Ajuster" });
            cmbFit.Items.Add(new ComboBoxItem { Tag = WallpaperFitMode.Stretch, Content = "Étirer" });
            cmbFit.Items.Add(new ComboBoxItem { Tag = WallpaperFitMode.Center, Content = "Centrer" });
            cmbFit.SelectedIndex = cfg.FitMode switch
            {
                WallpaperFitMode.Fit => 1,
                WallpaperFitMode.Stretch => 2,
                WallpaperFitMode.Center => 3,
                _ => 0
            };
            cmbFit.SelectionChanged += (_, _) =>
            {
                if (cmbFit.SelectedItem is ComboBoxItem cbi && cbi.Tag is WallpaperFitMode mode && cfg.FitMode != mode)
                {
                    cfg.FitMode = mode;
                    RenderMonitorCards();
                }
            };
            Grid.SetColumn(cmbFit, 1);

            var btnUrl = new System.Windows.Controls.Button
            {
                Content = "URL",
                FontSize = 11,
                Height = 26,
                Padding = new Thickness(10, 0, 10, 0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 39, 42)),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 250, 250)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnUrl.Click += (_, e) =>
            {
                e.Handled = true;
                OpenUrlDialog(mon);
            };
            Grid.SetColumn(btnUrl, 2);

            bottomGrid.Children.Add(lblFile);
            bottomGrid.Children.Add(cmbFit);
            bottomGrid.Children.Add(btnUrl);
            bottomBar.Child = bottomGrid;
            Grid.SetRow(bottomBar, 2);

            grid.Children.Add(topBar);
            grid.Children.Add(previewGrid);
            grid.Children.Add(bottomBar);
            card.Child = grid;

            // Click on top/preview opens file picker (unless image already handles pan/click)
            if (!hasPannableImage && cfg.Type != WallpaperType.Image)
                previewGrid.MouseLeftButtonUp += (_, _) => BrowseLocalFile(mon);
            topBar.MouseLeftButtonUp += (_, _) => BrowseLocalFile(mon);

            card.DragEnter += (_, e) =>
            {
                if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                    e.Effects = System.Windows.DragDropEffects.Copy;
            };
            card.Drop += (_, e) =>
            {
                if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                    if (files.Length > 0) SetLocalFile(mon, files[0]);
                }
            };

            Canvas.SetLeft(card, px + 4);
            Canvas.SetTop(card, py + 4);
            MonitorCanvas.Children.Add(card);
        }
    }

    private async void InitPreviewWebView(Microsoft.Web.WebView2.Wpf.WebView2 wv, MonitorInfoItem mon, MonitorWallpaperConfig cfg)
    {
        try
        {
            string userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyWallpaper", "WebView2");
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, userData);
            await wv.EnsureCoreWebView2Async(env);
            wv.CoreWebView2.IsMuted = true;
            wv.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            wv.CoreWebView2.Settings.AreDevToolsEnabled = false;

            wv.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(e.TryGetWebMessageAsString());
                    var root = doc.RootElement;
                    string? t = root.GetProperty("type").GetString();
                    if (t == "pan")
                    {
                        cfg.OffsetX = Math.Clamp(root.GetProperty("ox").GetDouble(), 0.0, 1.0);
                        cfg.OffsetY = Math.Clamp(root.GetProperty("oy").GetDouble(), 0.0, 1.0);
                    }
                    else if (t == "click")
                    {
                        Dispatcher.Invoke(() => OpenUrlDialog(mon));
                    }
                }
                catch { }
            };

            string objectFit = cfg.FitMode switch
            {
                WallpaperFitMode.Fit => "contain",
                WallpaperFitMode.Stretch => "fill",
                WallpaperFitMode.Center => "none",
                _ => "cover"
            };
            double ox = Math.Clamp(cfg.OffsetX, 0.0, 1.0);
            double oy = Math.Clamp(cfg.OffsetY, 0.0, 1.0);

            string panScript = $@"
let drag=false, moved=false, sx=0, sy=0, sox={ox.ToString(System.Globalization.CultureInfo.InvariantCulture)}, soy={oy.ToString(System.Globalization.CultureInfo.InvariantCulture)};
let ox=sox, oy=soy;
const v=document.getElementById('v');
document.addEventListener('mousedown', e => {{ drag=true; moved=false; sx=e.clientX; sy=e.clientY; sox=ox; soy=oy; }});
document.addEventListener('mousemove', e => {{
  if(!drag) return;
  const dx=e.clientX-sx, dy=e.clientY-sy;
  if(Math.Abs(dx)>4 || Math.Abs(dy)>4) moved=true;
  if(!moved) return;
  const w=Math.max(1, window.innerWidth), h=Math.max(1, window.innerHeight);
  ox = Math.max(0, Math.min(1, sox - dx / w));
  oy = Math.max(0, Math.min(1, soy - dy / h));
  if(v) v.style.objectPosition = (ox*100).toFixed(1)+'% '+(oy*100).toFixed(1)+'%';
  window.chrome.webview.postMessage(JSON.stringify({{type:'pan', ox, oy}}));
}});
document.addEventListener('mouseup', () => {{
  if(!drag) return;
  drag=false;
  if(!moved) window.chrome.webview.postMessage(JSON.stringify({{type:'click'}}));
}});";
            panScript = panScript.Replace("Math.Abs", "Math.abs");

            if (cfg.Type == WallpaperType.Video && File.Exists(cfg.Source))
            {
                string fileUrl = new Uri(cfg.Source).AbsoluteUri;
                string html = $@"<!DOCTYPE html><html><body style='margin:0;padding:0;overflow:hidden;background:#0a0a0b;cursor:move;'>
<video id='v' autoplay loop muted playsinline style='width:100vw;height:100vh;object-fit:{objectFit};object-position:{(ox*100).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}% {(oy*100).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}%;display:block;pointer-events:none;'><source src='{fileUrl}'/></video>
<script>{panScript}</script></body></html>";
                wv.CoreWebView2.NavigateToString(html);
            }
            else
            {
                string pathOnly = cfg.Source.Split('?')[0].ToLowerInvariant();
                if (pathOnly.EndsWith(".m3u8"))
                {
                    string jsUrl = System.Text.Json.JsonSerializer.Serialize(cfg.Source);
                    string html = $@"<!DOCTYPE html><html><body style='margin:0;padding:0;overflow:hidden;background:#0a0a0b;cursor:move;'>
<video id='v' autoplay muted playsinline style='width:100vw;height:100vh;object-fit:{objectFit};object-position:{(ox*100).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}% {(oy*100).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}%;display:block;pointer-events:none;'></video>
<script src='https://cdn.jsdelivr.net/npm/hls.js@1'></script>
<script>
const src={jsUrl};
if(typeof Hls!=='undefined'&&Hls.isSupported()){{
  const hls=new Hls({{lowLatencyMode:true,backBufferLength:15}});
  hls.loadSource(src); hls.attachMedia(document.getElementById('v'));
  hls.on(Hls.Events.MANIFEST_PARSED,()=>document.getElementById('v').play().catch(()=>{{}}));
}} else {{ const el=document.getElementById('v'); el.src=src; el.play().catch(()=>{{}}); }}
{panScript}
</script></body></html>";
                    wv.CoreWebView2.NavigateToString(html);
                }
                else if (pathOnly.EndsWith(".mp4") || pathOnly.EndsWith(".webm"))
                {
                    string safeUrl = System.Net.WebUtility.HtmlEncode(cfg.Source);
                    string html = $@"<!DOCTYPE html><html><body style='margin:0;padding:0;overflow:hidden;background:#0a0a0b;cursor:move;'>
<video id='v' autoplay loop muted playsinline style='width:100vw;height:100vh;object-fit:{objectFit};object-position:{(ox*100).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}% {(oy*100).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}%;display:block;pointer-events:none;'><source src='{safeUrl}'/></video>
<script>{panScript}</script></body></html>";
                    wv.CoreWebView2.NavigateToString(html);
                }
                else
                {
                    wv.CoreWebView2.Navigate(cfg.Source);
                }
            }
        }
        catch { }
    }

    private static BitmapImage? LoadPreviewBitmap(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = fs;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private void BrowseLocalFile(MonitorInfoItem mon)
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Choisir un média pour {mon.Title}",
            Filter = "Images & Vidéos|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif;*.mp4;*.webm;*.mkv;*.mov;*.avi;*.wmv|Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif|Vidéos|*.mp4;*.webm;*.mkv;*.mov;*.avi;*.wmv|Tous|*.*"
        };
        if (ofd.ShowDialog(this) == true)
        {
            SetLocalFile(mon, ofd.FileName);
        }
    }

    private void SetLocalFile(MonitorInfoItem mon, string filePath)
    {
        if (!File.Exists(filePath)) return;
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        WallpaperType type = VideoExtensions.Contains(ext)
            ? WallpaperType.Video
            : (ImageExtensions.Contains(ext) ? WallpaperType.Image : WallpaperType.Image);

        var existingFit = _configs.TryGetValue(mon.MonitorId, out var prev) ? prev.FitMode : WallpaperFitMode.Fill;
        _configs[mon.MonitorId] = new MonitorWallpaperConfig
        {
            MonitorId = mon.MonitorId,
            Type = type,
            Source = filePath,
            FitMode = existingFit,
            OffsetX = 0.5,
            OffsetY = 0.5,
            MuteAudio = true
        };
        SetStatus($"Sélectionné : {Path.GetFileName(filePath)}");
        RenderMonitorCards();
    }

    private void OpenUrlDialog(MonitorInfoItem mon)
    {
        _configs.TryGetValue(mon.MonitorId, out var existing);
        var dlg = new UrlDialog(existing) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            if (dlg.Cleared)
            {
                _configs.Remove(mon.MonitorId);
                if (_hosts.TryGetValue(mon.MonitorId, out var host))
                {
                    host.Close();
                    _hosts.Remove(mon.MonitorId);
                }
                SetStatus("Source supprimée.");
            }
            else
            {
                var existingFit = existing?.FitMode ?? WallpaperFitMode.Fill;
                _configs[mon.MonitorId] = new MonitorWallpaperConfig
                {
                    MonitorId = mon.MonitorId,
                    Type = dlg.SelectedType,
                    Source = dlg.ResultUrl,
                    RefreshSeconds = dlg.RefreshSeconds,
                    FitMode = existingFit,
                    MuteAudio = dlg.MuteAudio
                };
                _lastFetch[mon.MonitorId] = DateTime.MinValue;
                SetStatus("Source configurée — cliquez sur Appliquer.");
            }
            RenderMonitorCards();
        }
    }

    private void ApplyAllWallpapers()
    {
        int count = 0;

        foreach (var mon in _monitors)
        {
            if (!_configs.TryGetValue(mon.MonitorId, out var cfg) || cfg.Type == WallpaperType.None)
                continue;

            var host = GetOrCreateHost(mon);

            if (cfg.Type == WallpaperType.Image)
            {
                host.ShowImage(cfg.Source, cfg.FitMode, cfg.OffsetX, cfg.OffsetY);
                count++;
            }
            else if (cfg.Type == WallpaperType.Video)
            {
                host.ShowVideo(cfg.Source, cfg.FitMode, cfg.MuteAudio, cfg.OffsetX, cfg.OffsetY);
                count++;
            }
            else if (cfg.Type == WallpaperType.WebPage)
            {
                host.ShowWebPage(cfg.Source, cfg.MuteAudio, cfg.FitMode, cfg.OffsetX, cfg.OffsetY);
                count++;
            }
            else if (cfg.Type == WallpaperType.WebcamSnapshot)
            {
                _lastFetch[mon.MonitorId] = DateTime.MinValue;
                _ = FetchAndShowWebcamAsync(mon, cfg);
                count++;
            }
        }

        SaveCurrentConfig();
        SetStatus($"Appliqué sur {count} écran(s)", isSuccess: true);
    }

    private WallpaperHostForm GetOrCreateHost(MonitorInfoItem mon)
    {
        if (!_hosts.TryGetValue(mon.MonitorId, out var host))
        {
            host = new WallpaperHostForm(mon);
            host.Show();
            host.Attach();
            _hosts[mon.MonitorId] = host;
        }
        else
        {
            host.UpdateMonitor(mon);
        }
        return host;
    }

    private async void WebcamTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        foreach (var mon in _monitors)
        {
            if (!_configs.TryGetValue(mon.MonitorId, out var cfg) || cfg.Type != WallpaperType.WebcamSnapshot)
                continue;
            if (!_hosts.ContainsKey(mon.MonitorId))
                continue;

            var last = _lastFetch.GetValueOrDefault(mon.MonitorId, DateTime.MinValue);
            if ((now - last).TotalSeconds >= cfg.RefreshSeconds)
            {
                _lastFetch[mon.MonitorId] = now;
                await FetchAndShowWebcamAsync(mon, cfg);
            }
        }
    }

    private async Task FetchAndShowWebcamAsync(MonitorInfoItem mon, MonitorWallpaperConfig cfg)
    {
        if (!_inFlight.Add(mon.MonitorId)) return;
        try
        {
            bool toggle = !_toggleFile.GetValueOrDefault(mon.MonitorId, false);
            _toggleFile[mon.MonitorId] = toggle;
            string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyWallpaper", "cache");
            string dest = Path.Combine(cacheDir, $"cam_{mon.Index}_{(toggle ? "a" : "b")}.jpg");

            await WebcamFetcher.FetchFrameAsync(cfg.Source, dest);
            _cachedFrames[mon.MonitorId] = dest;
            if (_hosts.ContainsKey(mon.MonitorId))
            {
                var host = GetOrCreateHost(mon);
                host.ShowImage(dest, cfg.FitMode, cfg.OffsetX, cfg.OffsetY);
                SetStatus($"Webcam mise à jour à {DateTime.Now:HH:mm:ss}", isSuccess: true);
            }
            RenderMonitorCards();
        }
        catch (Exception ex)
        {
            SetStatus($"Webcam : {ex.Message}", isError: true);
        }
        finally
        {
            _inFlight.Remove(mon.MonitorId);
        }
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            RefreshMonitors();
            foreach (var mon in _monitors)
            {
                if (_hosts.TryGetValue(mon.MonitorId, out var host))
                {
                    host.UpdateMonitor(mon);
                }
            }
        });
    }

    private void MonitorCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderMonitorCards();

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshMonitors();
        SetStatus("Écrans actualisés");
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e) => ApplyAllWallpapers();

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void RestartApp()
    {
        string? exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe))
        {
            string args = IsVisible ? "" : "--minimized";
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
        }
        ExitApp();
    }

    private void ExitApp()
    {
        _forceExit = true;
        _webcamTimer.Stop();
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        foreach (var h in _hosts.Values) h.Close();
        _hosts.Clear();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_forceExit && _hosts.Count > 0)
        {
            e.Cancel = true;
            Hide();
            _trayIcon.Visible = true;
            _trayIcon.ShowBalloonTip(2500, "EasyWallpaper", "Vos fonds d'écran restent actifs. Clic droit sur l'icône pour quitter.", System.Windows.Forms.ToolTipIcon.None);
            return;
        }
        base.OnClosing(e);
        if (!_forceExit) ExitApp();
    }
}

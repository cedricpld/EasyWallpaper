namespace CGWallpaper;

public enum WallpaperType
{
    None,
    Image,
    Video,
    WebcamSnapshot,
    WebPage,
    WebImage
}

public enum WallpaperFitMode
{
    Fill,
    Fit,
    Stretch,
    Center
}

public class MonitorInfoItem
{
    public int Index { get; set; }
    public string MonitorId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsPrimary { get; set; }
    public string Title => $"Écran {Index} ({Width}×{Height})" + (IsPrimary ? " • Principal" : "");
}

public class MonitorWallpaperConfig
{
    public string MonitorId { get; set; } = "";
    public WallpaperType Type { get; set; } = WallpaperType.None;
    public string Source { get; set; } = "";
    public string Selector { get; set; } = "";
    public int RefreshSeconds { get; set; } = 10;
    public WallpaperFitMode FitMode { get; set; } = WallpaperFitMode.Fill;
    public double OffsetX { get; set; } = 0.5;
    public double OffsetY { get; set; } = 0.5;
    public double Softness { get; set; } = 0.0;
    public bool MuteAudio { get; set; } = true;
}

public class AppConfig
{
    public List<MonitorWallpaperConfig> Monitors { get; set; } = new();
    public WallpaperFitMode GlobalFitMode { get; set; } = WallpaperFitMode.Fill;
}

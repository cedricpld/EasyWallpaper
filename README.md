# EasyWallpaper

A multi-monitor desktop wallpaper manager for Windows 11. Assigns local images, looping videos, webcams, or live video streams independently to each display, with accurate aspect-ratio previews and interactive cropping.

---

## Features

- **Real-world monitor layout**: Automatically detects all connected displays (resolution, portrait/landscape orientation, relative virtual-desktop position) and renders them proportionally in the interface.
- **Per-monitor media support**:
  - Local images (`JPG`, `PNG`, `BMP`, `WebP`, `GIF`) via click or drag-and-drop.
  - Looping local videos (`MP4`, `WebM`, `MKV`, `MOV`, `AVI`, `WMV`), muted by default.
  - Real-time live streams: HLS streams (`.m3u8`), direct video URLs, YouTube / YouTube Live, or web pages.
  - Snapshot webcams (`JPG`, `MJPEG`) and webpage-extracted images (automatic Skaping 2160p / `og:image` detection, or custom HTML/CSS/Regex selector) with change detection and configurable refresh interval.
- **Independent fit and crop**:
  - Per-display fit modes: `Fill`, `Fit`, `Stretch`, and `Center`.
  - Real-time preview matching the exact aspect ratio of each monitor.
  - Click-and-drag directly inside the preview to pan and choose the visible region when an image or live stream is cropped.
- **System integration**:
  - Single-file standalone executable (`EasyWallpaper.exe`) with embedded icon.
  - Persistent system tray icon with `Open`, `Launch on startup`, `Restart`, and `Quit`.
  - Multi-monitor profiles: save and switch between named profiles capturing the combined setup (media sources, fit modes, crop offsets, and filters) of all screens at once, from the main window or the tray menu.
  - Built-in GitHub Releases update check (`Check for updates` in tray menu and automatic prompt on launch).

---

## Usage and Build

### Prerequisites

- Windows 10 / 11 (x64)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (and [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build from source)
- Microsoft Edge WebView2 Runtime (preinstalled on Windows 11)

### Build a standalone `.exe`

Run `Build-EasyWallpaper.bat` or execute:

```bat
dotnet publish EasyWallpaper.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

The resulting executable is output to `dist\EasyWallpaper.exe` and can be moved and launched anywhere on its own.

To launch minimized to the system tray (used by Windows auto-startup):

```bat
EasyWallpaper.exe --minimized
```

---

## Technical Architecture

### 1. Monitor Enumeration and DPI

Displays are enumerated through the native `IDesktopWallpaper` COM interface (`GetMonitorDevicePathCount`, `GetMonitorDevicePathAt`, `GetMonitorRect`) to obtain hardware device paths and virtual-screen bounds. `app.manifest` enables `PerMonitorV2` DPI awareness for pixel-accurate placement across mixed-DPI monitor setups.

### 2. Desktop Layer Rendering (Windows 11 24H2 / 26H2)

Instead of calling `IDesktopWallpaper::SetWallpaper` or `SystemParametersInfo` (which may be disabled on unactivated or policy-locked Windows sessions), EasyWallpaper hosts borderless WinForms windows (`WallpaperHostForm`) directly inside the Windows desktop window hierarchy:

1. Sends `0x052C` to `Progman` (`SendMessageTimeout`) to ensure the desktop worker layers are initialized.
2. Detects the active compositor layout:
   - Windows 10 / pre-24H2 Windows 11: parents to the top-level `WorkerW` following `SHELLDLL_DefView`.
   - Windows 11 24H2 / 26H2 (*Raised Desktop*): parents directly to `Progman` using Win32 styles `0x56010000` (`WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS | WS_TABSTOP`) and `0x08090080` (`WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_CONTROLPARENT | WS_EX_TOOLWINDOW`) along with `SetLayeredWindowAttributes(..., 255, LWA_ALPHA)`.
3. Positions each host window right behind `SHELLDLL_DefView` (the desktop icons layer) and pushes child `WorkerW` windows to `HWND_BOTTOM` so multiple monitors never occlude one another.
4. Listens to `SystemEvents.DisplaySettingsChanged` to re-attach host windows after resolution changes or sleep/wake cycles.

### 3. Media Playback and Live Streams

- **Images**: Loaded into memory (`Bitmap`) and rendered in `OnPaint` using `InterpolationMode.HighQualityBicubic`.
- **Videos, `.m3u8` streams, and web pages**: Rendered via `Microsoft.Web.WebView2`. HLS streams (`.m3u8`) use `hls.js` in low-latency mode with automatic recovery on network or media errors.
- **Interactive cropping (`OffsetX`, `OffsetY`)**: Normalized `[0.0, 1.0]` offsets (`0.5` centered by default) drive pixel offsets for GDI+ image rendering and CSS `object-position` percentages (formatted with `CultureInfo.InvariantCulture`) for HTML5/HLS video elements.

### 4. Persistence and Startup

Per-monitor settings (`MonitorId`, source type, URL/path, refresh interval, fit mode, `OffsetX`/`OffsetY`) are serialized as JSON to `%LOCALAPPDATA%\EasyWallpaper\config.json`. Enabling startup writes `"<path>\EasyWallpaper.exe" --minimized` to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

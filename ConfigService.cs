using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace CGWallpaper;

public static class ConfigService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EasyWallpaper");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRunName = "EasyWallpaper";

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                if (cfg.Profiles.Count == 0)
                {
                    cfg.Profiles.Add(new WallpaperProfile
                    {
                        Name = string.IsNullOrWhiteSpace(cfg.ActiveProfileName) ? "Défaut" : cfg.ActiveProfileName,
                        Monitors = cfg.Monitors ?? new()
                    });
                    cfg.ActiveProfileName = cfg.Profiles[0].Name;
                }
                return cfg;
            }
        }
        catch { }
        return new AppConfig
        {
            ActiveProfileName = "Défaut",
            Profiles = [new WallpaperProfile { Name = "Défaut" }]
        };
    }

    public static void Save(AppConfig cfg)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            string json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(AppRunName) != null;
        }
        catch { return false; }
    }

    public static void SetStartupEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return;
            if (enable)
            {
                string exePath = Environment.ProcessPath ?? "";
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(AppRunName, $"\"{exePath}\" --minimized");
            }
            else
            {
                key.DeleteValue(AppRunName, false);
            }
        }
        catch { }
    }

    public static void RegisterFileAssociation()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;

            using var extKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.ewp");
            extKey.SetValue("", "EasyWallpaper.Profile");

            using var progId = Registry.CurrentUser.CreateSubKey(@"Software\Classes\EasyWallpaper.Profile");
            progId.SetValue("", "EasyWallpaper Profile");

            using var iconKey = progId.CreateSubKey("DefaultIcon");
            iconKey.SetValue("", $"\"{exePath}\",0");

            using var cmdKey = progId.CreateSubKey(@"shell\open\command");
            cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");
        }
        catch { }
    }
}

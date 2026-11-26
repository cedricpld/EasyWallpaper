using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace CGWallpaper;

public record UpdateInfo(Version LatestVersion, string TagName, string DownloadUrl, string ReleaseUrl);

public static class UpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/cedricpld/EasyWallpaper/releases/latest";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    static UpdateService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("EasyWallpaper-Updater/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static Version GetCurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        string json = await _http.GetStringAsync(LatestReleaseApi, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string tag = root.TryGetProperty("tag_name", out var tProp) ? (tProp.GetString() ?? "") : "";
        string htmlUrl = root.TryGetProperty("html_url", out var hProp) ? (hProp.GetString() ?? "") : "";
        if (string.IsNullOrWhiteSpace(tag)) return null;

        string clean = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(clean, out var parsed)) return null;
        var remoteVer = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));

        if (remoteVer <= GetCurrentVersion()) return null;

        string? downloadUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var nProp) ? (nProp.GetString() ?? "") : "";
                if (name.Equals("EasyWallpaper.exe", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.TryGetProperty("browser_download_url", out var uProp) ? uProp.GetString() : null;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(downloadUrl)) return null;
        return new UpdateInfo(remoteVer, tag, downloadUrl, htmlUrl);
    }

    public static async Task DownloadAndApplyUpdateAsync(UpdateInfo update, bool relaunchMinimized)
    {
        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
            throw new InvalidOperationException("Chemin de l'exécutable introuvable.");

        string updateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EasyWallpaper",
            "update");
        Directory.CreateDirectory(updateDir);

        string newExePath = Path.Combine(updateDir, "EasyWallpaper.exe.new");
        byte[] bytes = await _http.GetByteArrayAsync(update.DownloadUrl);
        if (bytes.Length < 100_000)
            throw new InvalidOperationException("Fichier téléchargé invalide.");

        await File.WriteAllBytesAsync(newExePath, bytes);

        string cmdPath = Path.Combine(updateDir, "apply_update.cmd");
        string args = relaunchMinimized ? " --minimized" : "";
        string script = $@"@echo off
timeout /t 1 /nobreak >nul
:retry
move /y ""{newExePath}"" ""{currentExe}"" >nul 2>&1
if errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto retry
)
start """" ""{currentExe}""{args}
del ""%~f0""
";
        await File.WriteAllTextAsync(cmdPath, script);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cmdPath}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }
}

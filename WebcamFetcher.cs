using System.IO;
using System.Net.Http;

namespace CGWallpaper;

public static class WebcamFetcher
{
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    static WebcamFetcher()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        _http.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
    }

    public static async Task<string> FetchFrameAsync(string url, string destPath, CancellationToken ct = default)
    {
        string? dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        string ctHeader = resp.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);

        if (ctHeader.Contains("multipart/x-mixed-replace"))
        {
            byte[]? frame = await ReadMjpegFrameAsync(stream, 5 * 1024 * 1024, ct);
            if (frame == null || frame.Length < 100)
                throw new InvalidOperationException("Flux MJPEG : aucune image JPEG valide reçue.");
            await File.WriteAllBytesAsync(destPath, frame, ct);
        }
        else
        {
            await using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            byte[] data = ms.ToArray();
            if (data.Length < 100)
                throw new InvalidOperationException("Réponse image vide ou invalide.");
            await File.WriteAllBytesAsync(destPath, data, ct);
        }
        return destPath;
    }

    private static async Task<byte[]?> ReadMjpegFrameAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        var buf = new List<byte>(65536);
        byte[] readBuf = new byte[4096];
        int prev = -1;
        bool inJpeg = false;
        int totalRead = 0;

        while (totalRead < maxBytes)
        {
            int n = await stream.ReadAsync(readBuf, 0, readBuf.Length, ct);
            if (n <= 0) break;
            for (int i = 0; i < n; i++)
            {
                totalRead++;
                int b = readBuf[i];
                if (!inJpeg)
                {
                    if (prev == 0xFF && b == 0xD8)
                    {
                        inJpeg = true;
                        buf.Add(0xFF);
                        buf.Add(0xD8);
                    }
                }
                else
                {
                    buf.Add((byte)b);
                    if (prev == 0xFF && b == 0xD9)
                    {
                        return buf.ToArray();
                    }
                }
                prev = b;
            }
        }
        return null;
    }
}

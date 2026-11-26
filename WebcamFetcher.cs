using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

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

    public static async Task<(bool changed, string resolvedUrl)> FetchWebImageAsync(
        string pageUrl,
        string? selector,
        string? lastResolvedUrl,
        string destPath,
        CancellationToken ct = default)
    {
        string html = await _http.GetStringAsync(pageUrl, ct);
        string? resolved = ExtractImageUrlFromHtml(pageUrl, html, selector);
        if (string.IsNullOrWhiteSpace(resolved))
            throw new InvalidOperationException("Aucune image trouvée sur la page.");

        if (string.Equals(resolved, lastResolvedUrl, StringComparison.OrdinalIgnoreCase) && File.Exists(destPath))
            return (false, resolved);

        await FetchFrameAsync(resolved, destPath, ct);
        return (true, resolved);
    }

    public static string? ExtractImageUrlFromHtml(string pageUrl, string html, string? selector)
    {
        var baseUri = new Uri(pageUrl);
        string? raw = null;

        if (!string.IsNullOrWhiteSpace(selector))
        {
            string sel = selector.Trim();
            // 1. Custom regex with capture group
            if (sel.Contains('(') && sel.Contains(')'))
            {
                var m = Regex.Match(html, sel, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (m.Success && m.Groups.Count > 1) raw = m.Groups[1].Value;
            }
            // 2. CSS-like selector: tag#id, tag.class, tag[attr="val"], or tag
            if (raw == null)
            {
                raw = MatchSimpleSelector(html, sel);
            }
        }
        else
        {
            // Auto mode: Skaping full-res ImageMedia first
            var skaping = Regex.Match(html, @"new\s+ImageMedia\s*\(\s*\d+\s*,\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (skaping.Success)
            {
                raw = skaping.Groups[1].Value;
            }
            else
            {
                raw = MatchSimpleSelector(html, @"meta[property=""og:image""]")
                   ?? MatchSimpleSelector(html, @"meta[name=""twitter:image""]")
                   ?? MatchSimpleSelector(html, @"link[rel=""image_src""]")
                   ?? MatchSimpleSelector(html, "img");
            }
        }

        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = WebUtility.HtmlDecode(raw.Trim());
        if (raw.StartsWith("//")) raw = baseUri.Scheme + ":" + raw;
        return Uri.TryCreate(baseUri, raw, out var abs) ? abs.AbsoluteUri : null;
    }

    private static string? MatchSimpleSelector(string html, string selector)
    {
        string tag = "img";
        string? id = null;
        string? cls = null;
        string? attrName = null;
        string? attrVal = null;

        var attrMatch = Regex.Match(selector, @"\[([^=\]]+)=[""']?([^""'\]]+)[""']?\]");
        if (attrMatch.Success)
        {
            attrName = attrMatch.Groups[1].Value.Trim();
            attrVal = attrMatch.Groups[2].Value.Trim();
            selector = selector.Remove(attrMatch.Index, attrMatch.Length);
        }

        var idMatch = Regex.Match(selector, @"#([A-Za-z0-9_-]+)");
        if (idMatch.Success)
        {
            id = idMatch.Groups[1].Value;
            selector = selector.Remove(idMatch.Index, idMatch.Length);
        }

        var clsMatch = Regex.Match(selector, @"\.([A-Za-z0-9_-]+)");
        if (clsMatch.Success)
        {
            cls = clsMatch.Groups[1].Value;
            selector = selector.Remove(clsMatch.Index, clsMatch.Length);
        }

        if (!string.IsNullOrWhiteSpace(selector)) tag = selector.Trim();

        var tagRegex = new Regex($@"<{Regex.Escape(tag)}\b([^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match m in tagRegex.Matches(html))
        {
            string attrs = m.Groups[1].Value;
            if (id != null && !Regex.IsMatch(attrs, $@"\bid\s*=\s*[""']{Regex.Escape(id)}[""']", RegexOptions.IgnoreCase))
                continue;
            if (cls != null && !Regex.IsMatch(attrs, $@"\bclass\s*=\s*[""'][^""']*\b{Regex.Escape(cls)}\b[^""']*[""']", RegexOptions.IgnoreCase))
                continue;
            if (attrName != null && attrVal != null &&
                !Regex.IsMatch(attrs, $@"\b{Regex.Escape(attrName)}\s*=\s*[""']{Regex.Escape(attrVal)}[""']", RegexOptions.IgnoreCase))
                continue;

            var urlAttr = Regex.Match(attrs, @"\b(?:src|content|href|data-src)\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (urlAttr.Success) return urlAttr.Groups[1].Value;
        }
        return null;
    }
}

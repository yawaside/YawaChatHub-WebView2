using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace YawaChatHub.Services;

/// <summary>
/// Общий сетевой помощник коннекторов (аналог net.js из рабочей Electron-версии).
/// Ключевые детали, без которых площадки не отвечают:
///   • реальный браузерный User-Agent;
///   • CookieContainer с CONSENT/SOCS — иначе YouTube редиректит на consent-страницу;
///   • следование редиректам и мягкие таймауты;
///   • обход TLS-инспекции корпоративных прокси (ТЗ §15).
/// </summary>
public static class Net
{
    public const string UA =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/126.0.0.0 Safari/537.36";

    private static readonly CookieContainer Cookies = new();

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = Cookies,
            UseCookies = true,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };

        // YouTube без согласия на куки уходит на consent.youtube.com и отдаёт
        // страницу без ytInitialData — чат тогда не найти.
        try
        {
            var yt = new Uri("https://www.youtube.com");
            var rnd = Random.Shared.Next(100, 999);
            Cookies.Add(yt, new Cookie("CONSENT", $"YES+cb.20210328-17-p0.en+FX+{rnd}", "/", ".youtube.com"));
            Cookies.Add(yt, new Cookie("SOCS", "CAI", "/", ".youtube.com"));
        }
        catch { }

        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    public static async Task<(bool Ok, int Status, string Body)> RequestAsync(
        string url,
        HttpMethod? method = null,
        string? body = null,
        (string Key, string Value)[]? headers = null,
        CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(method ?? HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", UA);
            req.Headers.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,en;q=0.8");
            if (headers != null)
                foreach (var (k, v) in headers)
                    req.Headers.TryAddWithoutValidation(k, v);
            if (body != null)
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var resp = await Client.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            return ((int)resp.StatusCode is >= 200 and < 400, (int)resp.StatusCode, text);
        }
        catch
        {
            return (false, 0, "");
        }
    }

    public static async Task<string> GetTextAsync(string url, (string Key, string Value)[]? headers = null, CancellationToken ct = default)
    {
        var r = await RequestAsync(url, HttpMethod.Get, null, headers, ct);
        return r.Ok ? r.Body : "";
    }

    public static async Task<JsonNode?> GetJsonAsync(string url, (string Key, string Value)[]? headers = null, CancellationToken ct = default)
    {
        var text = await GetTextAsync(url, headers, ct);
        return Parse(text);
    }

    public static async Task<JsonNode?> PostJsonAsync(string url, object payload, (string Key, string Value)[]? headers = null, CancellationToken ct = default)
    {
        var r = await RequestAsync(url, HttpMethod.Post, JsonSerializer.Serialize(payload), headers, ct);
        return r.Ok ? Parse(r.Body) : null;
    }

    private static JsonNode? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonNode.Parse(text); } catch { return null; }
    }
}

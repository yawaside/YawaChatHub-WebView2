using System.Net;
using System.Text.Json;

namespace YawaChatHub;

/// <summary>
/// Автоматическая авторизация чат-бота.
///
/// БЕЗОПАСНОСТЬ: используется ТОЛЬКО публичный client_id — секретные ключи
/// в десктоп-сборку не попадают. Вход — только в браузере по умолчанию,
/// ни одного окна WebView2 для OAuth не открывается.
///
/// TWITCH — OAuth Device Code Flow (RFC 8628). Локального HTTP-слушателя
/// НЕТ ВООБЩЕ: приложение просит device-код у Twitch, открывает
/// twitch.tv/activate в браузере, кладёт код в буфер обмена и опрашивает
/// токен, пока пользователь подтверждает вход. Так исключён целый класс
/// проблем loopback-серверов: «127.0.0.1 отказано в подключении», занятые
/// порты, залипшие процессы в трее, прокси/антивирус, netsh addurlacl.
///
/// VK — implicit flow на стандартный https://oauth.vk.com/blank.html
/// (разрешён VK по умолчанию; loopback-адреса VK не принимает). Обязательный
/// параметр revoke=1: без него при ПОВТОРНОЙ авторизации VK отвечает
/// {"error":"invalid_request","error_description":"Security Error"} — именно
/// та ошибка, что приходила на каждой попытке. Токен VK возвращает во
/// фрагменте адреса blank.html: приложение забирает адрес страницы из буфера
/// обмена (Ctrl+L, Ctrl+C на странице) и проверяет токен у площадки.
///
/// Диагностика: %LOCALAPPDATA%\YawaChatHub\oauth.log (токены не пишем).
/// </summary>
public static class AppAuth
{
    /* ---------- зарегистрированные приложения ---------- */
    public const string TwitchClientId = "3qcjmtqkoobdcrrxzqbcw75n7egr8n";

    /// <summary>
    /// ID приложения VK (Standalone). Задаётся пользователем в настройках:
    /// прежний вшитый ID VK отклоняет на уровне самого приложения — на любой
    /// запрос, даже без cookies и с revoke, отвечает
    /// {"error":"invalid_request","error_description":"Security Error"}.
    /// Живые ID дают другие ответы, то есть починить это кодом нельзя:
    /// нужно своё приложение VK. Подключает HostApi из settings.json.
    /// </summary>
    public static string VkClientId = "";

    private const string TwitchScopes =
        "chat:read chat:edit moderator:manage:chat_messages moderator:manage:banned_users " +
        "moderator:manage:chat_settings channel:moderate";

    public sealed record AuthResult(string Account, string Token);

    /* ---------- буфер обмена (подключает HostApi, UI-поток STA) ---------- */
    public static Func<string?>? ClipboardReader;
    public static Action<string>? ClipboardWriter;

    /// <summary>Полный цикл авторизации. Бросает исключение с понятной причиной.</summary>
    public static async Task<AuthResult> AuthorizeAsync(string platform, CancellationToken ct = default)
    {
        return platform switch
        {
            "twitch" => await TwitchDeviceAuthorizeAsync(ct),
            "vkplay" => await VkAuthorizeAsync(ct),
            _ => throw new InvalidOperationException($"Неизвестная площадка {platform}"),
        };
    }

    /* ============================ TWITCH: Device Code Flow ============================ */

    /// <summary>
    /// Вход Twitch по device-коду. Никаких redirect_uri и локальных серверов:
    /// пользователь подтверждает код на twitch.tv/activate в браузере по
    /// умолчанию, приложение тем временем опрашивает токен.
    /// </summary>
    private static async Task<AuthResult> TwitchDeviceAuthorizeAsync(CancellationToken ct)
    {
        using var http = NewHttp();

        // 1) запрашиваем device-код
        OAuthLog.Write("[start] twitch: запрашиваем device-код");
        var (status, body) = await Post(http, "https://id.twitch.tv/oauth2/device",
            new Dictionary<string, string>
            {
                ["client_id"] = TwitchClientId,
                ["scopes"] = TwitchScopes,
            }, ct);
        if (status != HttpStatusCode.OK)
            throw new InvalidOperationException($"Twitch не выдал device-код: {body}");

        using var init = JsonDocument.Parse(body);
        var deviceCode = init.RootElement.GetProperty("device_code").GetString()
            ?? throw new InvalidOperationException("Twitch не вернул device_code");
        var userCode = init.RootElement.GetProperty("user_code").GetString() ?? "";
        var verifyUri = init.RootElement.GetProperty("verification_uri").GetString()
            ?? "https://www.twitch.tv/activate";
        var interval = init.RootElement.TryGetProperty("interval", out var iv) ? iv.GetInt32() : 5;
        if (interval < 3) interval = 3;
        var expiresIn = init.RootElement.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 900;

        // 2) открываем браузер на странице активации; код — уже в буфере обмена
        OAuthLog.Write($"[device] twitch: код {userCode}, страница {verifyUri}");
        try { ClipboardWriter?.Invoke(userCode); } catch { }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(verifyUri) { UseShellExecute = true });
        }
        catch
        {
            throw new InvalidOperationException("Не удалось открыть браузер");
        }

        // 3) опрашиваем токен до подтверждения пользователем
        var deadline = DateTime.UtcNow.AddSeconds(expiresIn);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(interval * 1000, ct);

            var (pStatus, pBody) = await Post(http, "https://id.twitch.tv/oauth2/token",
                new Dictionary<string, string>
                {
                    ["client_id"] = TwitchClientId,
                    ["device_code"] = deviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                }, ct);

            if (pStatus == HttpStatusCode.OK)
            {
                using var granted = JsonDocument.Parse(pBody);
                var token = granted.RootElement.GetProperty("access_token").GetString()
                    ?? throw new InvalidOperationException("Twitch не вернул access_token");
                OAuthLog.Write("[token] twitch: токен получен, проверяем…");
                var account = await VerifyAsync("twitch", token, ct);
                OAuthLog.Write($"[ok] twitch: бот подключён как {account}");
                return new AuthResult(account, token);
            }

            if (pBody.Contains("authorization_pending", StringComparison.OrdinalIgnoreCase))
                continue;   // пользователь ещё подтверждает — ждём интервал
            if (pBody.Contains("slow_down", StringComparison.OrdinalIgnoreCase))
            {
                interval += 5;
                continue;   // нас просят реже опрашивать
            }

            OAuthLog.Write($"[error] twitch: {pBody}");
            throw new InvalidOperationException($"Twitch отклонил вход: {pBody}");
        }

        OAuthLog.Write("[timeout] twitch: код не подтверждён вовремя");
        throw new InvalidOperationException("Код не подтверждён на странице twitch.tv/activate — нажмите «Войти» ещё раз");
    }

    /* ================================ VK: blank.html ================================ */

    /// <summary>
    /// Вход VK. redirect_uri — стандартный https://oauth.vk.com/blank.html
    /// (разрешён VK по умолчанию; loopback-адреса VK не принимает).
    /// ОБЯЗАТЕЛЕН revoke=1: иначе при повторной авторизации VK уходит на
    /// error?err=2 с {"error":"invalid_request","error_description":
    /// "Security Error"} — это задokументированный сценарий VK SDK.
    /// Токен VK отдаёт во фрагменте адреса; приложение читает адрес страницы
    /// из буфера обмена (пользователь на странице жмёт Ctrl+L, Ctrl+C).
    /// </summary>
    private static async Task<AuthResult> VkAuthorizeAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(VkClientId))
        {
            OAuthLog.Write("[fail] vkplay: не задан ID приложения VK");
            throw new InvalidOperationException(
                "Укажите ID своего приложения VK (тип Standalone) в разделе «Чат-бот»");
        }

        const string blank = "https://oauth.vk.com/blank.html";
        var authUrl =
            "https://oauth.vk.com/authorize" +
            $"?client_id={VkClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(blank)}" +
            "&response_type=token" +
            "&scope=offline" +
            "&revoke=1" +
            "&display=page&v=5.199";

        OAuthLog.Write("[start] vkplay: redirect_uri=blank.html (+revoke), ждём адрес из буфера обмена");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(authUrl) { UseShellExecute = true });
            OAuthLog.Write($"[browser] {authUrl}");
        }
        catch (Exception ex)
        {
            OAuthLog.Write($"[fail] не удалось открыть браузер: {ex.Message}");
            throw new InvalidOperationException("Не удалось открыть браузер");
        }

        var reader = ClipboardReader;
        var deadline = DateTime.UtcNow.AddMinutes(5);
        string? seen = reader?.Invoke();

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(400, ct);

            var text = reader?.Invoke();
            if (string.IsNullOrWhiteSpace(text) || text == seen) continue;
            seen = text;

            var token = ExtractVkToken(text!);
            if (token == null) continue;

            OAuthLog.Write("[token] vkplay: токен получен из буфера обмена, проверяем…");
            var account = await VerifyAsync("vkplay", token, ct);
            OAuthLog.Write($"[ok] vkplay: бот подключён как {account}");
            return new AuthResult(account, token);
        }

        OAuthLog.Write("[timeout] vkplay: токен так и не появился в буфере обмена");
        throw new InvalidOperationException(
            "VK: после входа скопируйте адресную строку страницы (Ctrl+L, затем Ctrl+C)");
    }

    /// Токен из адреса blank.html (#access_token=…) либо «голая» строка токена.
    private static string? ExtractVkToken(string text)
    {
        text = text.Trim();
        var m = System.Text.RegularExpressions.Regex.Match(text, @"access_token=([^&\s#]+)");
        if (m.Success) return m.Groups[1].Value;
        if (text.Length >= 40 && !text.Contains(' ')
            && !text.Contains("http", StringComparison.OrdinalIgnoreCase)
            && System.Text.RegularExpressions.Regex.IsMatch(text, "^[A-Za-z0-9._-]+$"))
            return text;
        return null;
    }

    /* ========================== проверка токена у площадки ========================== */

    /// <summary>Проверка токена у площадки и получение имени аккаунта.</summary>
    public static async Task<string> VerifyAsync(string platform, string token, CancellationToken ct = default)
    {
        token = token.Replace("oauth:", "", StringComparison.OrdinalIgnoreCase).Trim();
        using var http = NewHttp();

        if (platform == "twitch")
        {
            http.DefaultRequestHeaders.Add("Authorization", $"OAuth {token}");
            var r = await http.GetAsync("https://id.twitch.tv/oauth2/validate", ct);
            var body = await r.Content.ReadAsStringAsync(ct);
            if (!r.IsSuccessStatusCode) throw new InvalidOperationException("Twitch отклонил токен");
            using var doc = JsonDocument.Parse(body);
            return Net.FindString(doc.RootElement, "login")
                   ?? throw new InvalidOperationException("Twitch не вернул аккаунт");
        }

        if (platform == "vkplay")
        {
            http.DefaultRequestHeaders.Add("Origin", "https://live.vkvideo.ru");
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            var r = await http.GetAsync("https://api.live.vkvideo.ru/v1/user/current", ct);
            var body = await r.Content.ReadAsStringAsync(ct);
            if (r.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(body);
                var nick = Net.FindString(doc.RootElement, "nick") ?? Net.FindString(doc.RootElement, "displayName");
                if (!string.IsNullOrEmpty(nick)) return nick!;
            }

            // запасной путь: обычный профиль VK
            var vk = await http.GetStringAsync(
                $"https://api.vk.com/method/users.get?access_token={Uri.EscapeDataString(token)}&v=5.199", ct);
            using var vkDoc = JsonDocument.Parse(vk);
            var first = Net.FindString(vkDoc.RootElement, "first_name");
            var last = Net.FindString(vkDoc.RootElement, "last_name");
            if (!string.IsNullOrEmpty(first)) return $"{first} {last}".Trim();

            throw new InvalidOperationException("VK отклонил токен");
        }

        throw new InvalidOperationException($"Неизвестная площадка {platform}");
    }

    /* ================================ утилиты ================================ */

    private static HttpClient NewHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(Net.UA);
        return http;
    }

    private static async Task<(HttpStatusCode, string)> Post(
        HttpClient http, string url, Dictionary<string, string> form, CancellationToken ct)
    {
        var r = await http.PostAsync(url, new FormUrlEncodedContent(form), ct);
        var body = await r.Content.ReadAsStringAsync(ct);
        return (r.StatusCode, body);
    }

    /// <summary>Диагностический журнал входа: %LOCALAPPDATA%\YawaChatHub\oauth.log</summary>
    internal static class OAuthLog
    {
        private static readonly object Gate = new();
        public static string Path => System.IO.Path.Combine(Program.AppDir, "oauth.log");

        public static void Write(string line)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Program.AppDir);
                    File.AppendAllText(Path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
                }
            }
            catch { /* журнал не критичен */ }
        }
    }
}

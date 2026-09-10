using System.Net;
using System.Text;
using System.Text.Json;

namespace YawaChatHub;

/// <summary>
/// Автоматическая авторизация чат-бота (OAuth implicit flow).
///
/// БЕЗОПАСНОСТЬ: используется ТОЛЬКО публичный client_id. Секретные ключи
/// приложений в десктоп-сборку не попадают — их можно извлечь из exe,
/// поэтому implicit flow здесь единственный правильный вариант.
///
/// Как это работает:
///   1. поднимаем локальный слушатель http://127.0.0.1:PORT/oauth;
///   2. открываем браузер на странице авторизации площадки;
///   3. площадка возвращает токен на зарегистрированный redirect (сайт-мост);
///   4. страница-мост пересылает токен на локальный слушатель;
///   5. проверяем токен у площадки и отдаём имя аккаунта в приложение.
/// </summary>
public static class AppAuth
{
    /* ---------- зарегистрированные приложения ---------- */
    public const string TwitchClientId = "3qcjmtqkoobdcrrxzqbcw75n7egr8n";
    public const string VkClientId = "54759783";
    public const string RedirectUri = "https://yawachathub.netlify.app/";

    private const string TwitchScopes =
        "chat:read chat:edit moderator:manage:chat_messages moderator:manage:banned_users " +
        "moderator:manage:chat_settings channel:moderate";

    private static readonly int[] Ports = { 8123, 8124, 8125, 8126 };

    public sealed record AuthResult(string Account, string Token);

    /// <summary>Полный цикл авторизации. Бросает исключение с понятной причиной.</summary>
    public static async Task<AuthResult> AuthorizeAsync(string platform, CancellationToken ct = default)
    {
        var state = Guid.NewGuid().ToString("N");

        using var listener = new HttpListener();
        int port = 0;
        foreach (var p in Ports)
        {
            try
            {
                listener.Prefixes.Clear();
                listener.Prefixes.Add($"http://127.0.0.1:{p}/");
                listener.Start();
                port = p;
                break;
            }
            catch { /* порт занят — пробуем следующий */ }
        }
        if (port == 0) throw new InvalidOperationException("Не удалось открыть локальный порт для авторизации");

        var authUrl = BuildAuthUrl(platform, state, port);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(authUrl) { UseShellExecute = true });
        }
        catch
        {
            throw new InvalidOperationException("Не удалось открыть браузер");
        }

        // ждём ответ страницы-моста (даём пользователю время войти)
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));

        string? token = null;
        while (token == null && !timeout.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                var task = listener.GetContextAsync();
                var done = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, timeout.Token));
                if (done != task) break;
                ctx = task.Result;
            }
            catch { break; }

            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var q = ctx.Request.QueryString;

            // preflight от страницы-моста
            ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
            ctx.Response.AddHeader("Access-Control-Allow-Headers", "*");
            if (ctx.Request.HttpMethod == "OPTIONS") { ctx.Response.StatusCode = 204; ctx.Response.Close(); continue; }

            if (path.StartsWith("/oauth", StringComparison.OrdinalIgnoreCase))
            {
                var got = q["access_token"] ?? q["token"];
                var gotState = q["state"] ?? "";
                var error = q["error_description"] ?? q["error"];

                if (!string.IsNullOrEmpty(error))
                {
                    Reply(ctx, "Авторизация отклонена. Можно закрыть вкладку.");
                    throw new InvalidOperationException($"Площадка отклонила вход: {error}");
                }
                if (!string.IsNullOrEmpty(got) && gotState.StartsWith(state, StringComparison.Ordinal))
                {
                    token = got;
                    Reply(ctx, "Готово! Бот подключён — вернитесь в YawaChatHub.");
                    continue;
                }
            }

            Reply(ctx, "YawaChatHub ожидает авторизацию…");
        }

        try { listener.Stop(); } catch { }
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("Авторизация не завершена — окно ожидания закрыто");

        var account = await VerifyAsync(platform, token!, ct);
        return new AuthResult(account, token!);
    }

    private static string BuildAuthUrl(string platform, string state, int port) => platform switch
    {
        "twitch" =>
            "https://id.twitch.tv/oauth2/authorize" +
            $"?client_id={TwitchClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            "&response_type=token" +
            $"&scope={Uri.EscapeDataString(TwitchScopes)}" +
            "&force_verify=true" +
            $"&state={state}.{port}",

        "vkplay" =>
            "https://oauth.vk.com/authorize" +
            $"?client_id={VkClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            "&response_type=token" +
            "&scope=offline" +
            "&display=page&v=5.199" +
            $"&state={state}.{port}",

        _ => throw new InvalidOperationException($"Неизвестная площадка {platform}"),
    };

    /// <summary>Проверка токена у площадки и получение имени аккаунта.</summary>
    public static async Task<string> VerifyAsync(string platform, string token, CancellationToken ct = default)
    {
        token = token.Replace("oauth:", "", StringComparison.OrdinalIgnoreCase).Trim();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(Net.UA);

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

    private static void Reply(HttpListenerContext ctx, string message)
    {
        try
        {
            var html =
                "<!doctype html><meta charset=utf-8><title>YawaChatHub</title>" +
                "<body style=\"margin:0;height:100vh;display:flex;align-items:center;justify-content:center;" +
                "background:#0a0b13;color:#eceef6;font-family:Segoe UI,system-ui,sans-serif\">" +
                $"<div style=\"text-align:center\"><div style=\"width:44px;height:44px;margin:0 auto 14px;border-radius:12px;" +
                "background:linear-gradient(135deg,#8b5cf6,#6366f1)\"></div>" +
                $"<p style=\"font-size:15px\">{WebUtility.HtmlEncode(message)}</p></div></body>";
            var bytes = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes);
            ctx.Response.Close();
        }
        catch { }
    }
}

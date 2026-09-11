using System.Net;
using System.Net.Sockets;
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
/// Как это работает (БЕЗ внешних сайтов — вся цепочка локальная):
///   1. поднимаем локальный слушатель http://127.0.0.1:PORT/oauth;
///   2. открываем СИСТЕМНЫЙ браузер по умолчанию на странице авторизации;
///   3. redirect_uri — сам loopback. Twitch: http://127.0.0.1 (литеральный
///      IP), VK: http://localhost (VK не принимает IP-адреса в redirect).
///      Токен приходит во фрагменте (#access_token=...), который серверу не
///      отправляется, поэтому слушатель отдаёт крошечную страницу-мост: она
///      читает фрагмент и возвращается на тот же origin параметром запроса.
///      НИКАКИХ внешних сайтов, CORS и Private Network Access — токен не
///      может «не дойти». Требование: в консоли Twitch зарегистрированы
///      http://127.0.0.1:8123/oauth … :8126/oauth, в консоли VK —
///      http://localhost:8123/oauth … :8126/oauth (без этого площадки
///      отвечают ошибкой redirect_uri / "Security Error"). Каждый шаг
///      пишется в %LOCALAPPDATA%\YawaChatHub\oauth.log.
///   4. проверяем токен у площадки и отдаём имя аккаунта в приложение.
///
/// Важно: слушатель — на чистом TcpListener, а НЕ HttpListener.
/// HttpListener (http.sys) требует резервирования URL через `netsh addurlacl`
/// даже для 127.0.0.1 и без прав администратора Start() просто падает —
/// из-за этого вход «сразу просил ввести токен». TcpListener слушает
/// loopback без каких-либо прав и настроек.
/// </summary>
public static class AppAuth
{
    /* ---------- зарегистрированные приложения ---------- */
    public const string TwitchClientId = "3qcjmtqkoobdcrrxzqbcw75n7egr8n";
    public const string VkClientId = "54759783";

    private const string TwitchScopes =
        "chat:read chat:edit moderator:manage:chat_messages moderator:manage:banned_users " +
        "moderator:manage:chat_settings channel:moderate";

    private static readonly int[] Ports = { 8123, 8124, 8125, 8126 };

    public sealed record AuthResult(string Account, string Token);

    /// Чтение буфера обмена в UI-потоке хоста (нужно VK, см. VkAuthorizeAsync).
    public static Func<string?>? ClipboardReader;

    /// <summary>Полный цикл авторизации. Бросает исключение с понятной причиной.
    /// Каждый шаг пишется в oauth.log — если вход снова «зависнет», файл
    /// покажет, на каком этапе цепочки он оборвался.</summary>
    public static async Task<AuthResult> AuthorizeAsync(string platform, CancellationToken ct = default)
    {
        // VK не принимает loopback-адреса в redirect_uri: у Standalone-приложений
        // разрешён только https://oauth.vk.com/blank.html, всё остальное площадка
        // отклоняет как {"error":"invalid_request","error_description":"Security Error"}.
        if (platform == "vkplay") return await VkAuthorizeAsync(ct);

        var state = Guid.NewGuid().ToString("N");

        using var server = LoopbackHttp.Start();
        var authUrl = BuildAuthUrl(platform, state, server.Port);
        OAuthLog.Write($"[start] {platform}: слушатель на порту {server.Port}");

        // Вход ТОЛЬКО в браузере по умолчанию — никаких окон WebView2 внутри приложения
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

        // ждём ответ страницы-моста (даём пользователю время войти)
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));

        string? token = null;
        while (token == null && !timeout.IsCancellationRequested)
        {
            LoopbackHttp.Request? req;
            try
            {
                req = await server.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) { break; }

            OAuthLog.Write($"[req] {req.Method} {req.Path} параметры: [{string.Join(", ", req.QueryKeys())}]");

            // preflight от страницы-моста
            if (req.Method == "OPTIONS")
            {
                await req.RespondAsync(204, null);
                continue;
            }

            if (req.Path.StartsWith("/oauth", StringComparison.OrdinalIgnoreCase))
            {
                var got = req.Query("access_token") ?? req.Query("token");
                var gotState = req.Query("state") ?? "";
                var error = req.Query("error_description") ?? req.Query("error");

                if (!string.IsNullOrEmpty(error))
                {
                    OAuthLog.Write($"[error] площадка отклонила: {error}");
                    await req.RespondAsync(200, "Авторизация отклонена. Можно закрыть вкладку.");
                    throw new InvalidOperationException($"Площадка отклонила вход: {error}");
                }
                if (!string.IsNullOrEmpty(got) && gotState.StartsWith(state, StringComparison.Ordinal))
                {
                    token = got;
                    OAuthLog.Write($"[token] получен ({token.Length} символов), state совпадает");
                    await req.RespondAsync(200, "Готово! Бот подключён — вернитесь в YawaChatHub.");
                    continue;
                }

                // Площадка пришла на loopback с токеном во фрагменте
                // (#access_token=…), который браузер НЕ отправляет на сервер.
                // Отдаём страницу-мост: она читает фрагмент и возвращается
                // сюда же параметром запроса — тот же origin: ни CORS,
                // ни Private Network Access, ни внешних сайтов.
                await req.RespondAsync(200, null, HashBridgePage);
                continue;
            }

            await req.RespondAsync(200, "YawaChatHub ожидает авторизацию…");
        }

        if (string.IsNullOrEmpty(token))
        {
            OAuthLog.Write("[timeout] 3 минуты прошли, токен не получен (браузер так и не вернулся на loopback)");
            throw new InvalidOperationException(
                $"Авторизация не завершена за 3 минуты. Подробности: {OAuthLog.Path}");
        }

        try
        {
            OAuthLog.Write($"[verify] проверяем токен у площадки {platform}…");
            var account = await VerifyAsync(platform, token!, ct);
            OAuthLog.Write($"[ok] {platform}: бот подключён как {account}");
            return new AuthResult(account, token!);
        }
        catch (Exception ex)
        {
            OAuthLog.Write($"[verify-fail] {ex.Message}");
            throw;
        }
    }

    /// <summary>Диагностический журнал входа: %LOCALAPPDATA%\YawaChatHub\oauth.log</summary>
    internal static class OAuthLog
    {
        private static readonly object Gate = new();
        // System.IO.Path — полное имя обязательно: свойство называется «Path»
        // и само затеняет тип, иначе CS1061 'string does not contain Combine'
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

    /// <summary>
    /// Вход VK. redirect_uri — стандартный https://oauth.vk.com/blank.html:
    /// единственный адрес, который VK разрешает Standalone-приложению без
    /// настройки в консоли (любой loopback площадка отклоняет как
    /// {"error":"invalid_request","error_description":"Security Error"}).
    /// Токен VK кладёт во фрагмент адреса, а фрагмент на сервер не уходит,
    /// поэтому приложение забирает адрес из буфера обмена: на странице
    /// blank.html пользователь нажимает Ctrl+L, Ctrl+C. Ручной ввод токена
    /// остаётся запасным путём.
    /// </summary>
    private static async Task<AuthResult> VkAuthorizeAsync(CancellationToken ct)
    {
        const string blank = "https://oauth.vk.com/blank.html";
        var authUrl =
            "https://oauth.vk.com/authorize" +
            $"?client_id={VkClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(blank)}" +
            "&response_type=token" +
            "&scope=offline" +
            "&display=page&v=5.199";

        OAuthLog.Write("[start] vkplay: redirect_uri=blank.html, ждём адрес из буфера обмена");
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
        var deadline = DateTime.UtcNow.AddMinutes(3);
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

    /// Адрес на наш слушатель = redirect_uri (Twitch разрешает loopback-IP).
    private static string LoopbackRedirectUri(int port) => $"http://127.0.0.1:{port}/oauth";

    private static string BuildAuthUrl(string platform, string state, int port) => platform switch
    {
        // Twitch идёт напрямую на loopback; VK обрабатывается в VkAuthorizeAsync.
        "twitch" =>
            "https://id.twitch.tv/oauth2/authorize" +
            $"?client_id={TwitchClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(LoopbackRedirectUri(port))}" +
            "&response_type=token" +
            $"&scope={Uri.EscapeDataString(TwitchScopes)}" +
            "&force_verify=true" +
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

    /* ================= мини-HTTP на TcpListener (без urlacl и прав) ================= */

    /// Страница-мост: извлекает токен из location.hash и возвращает его
    /// параметром запроса на тот же loopback-origin. Для Twitch.
    private const string HashBridgePage = """
        <!doctype html><meta charset=utf-8><title>YawaChatHub</title>
        <body style="margin:0;height:100vh;display:flex;align-items:center;justify-content:center;background:#0a0b13;color:#eceef6;font-family:Segoe UI,system-ui,sans-serif">
        <div style="text-align:center"><div style="width:44px;height:44px;margin:0 auto 14px;border-radius:12px;background:linear-gradient(135deg,#8b5cf6,#6366f1)"></div>
        <p id=t style="font-size:15px">Завершаем вход…</p></div>
        <script>
        var p = new URLSearchParams(location.hash.slice(1));
        var t = p.get('access_token') || p.get('token');
        var e = p.get('error_description') || p.get('error');
        var s = p.get('state') || '';
        if (e) location.replace('/oauth?error=' + encodeURIComponent(e));
        else if (t) location.replace('/oauth?access_token=' + encodeURIComponent(t) + '&state=' + encodeURIComponent(s));
        else document.getElementById('t').textContent = 'Токен не найден — повторите вход из YawaChatHub.';
        </script>
        </body>
        """;

    private sealed class LoopbackHttp : IDisposable
    {
        private readonly List<TcpListener> _listeners;
        public int Port { get; }

        private LoopbackHttp(List<TcpListener> listeners, int port)
        {
            _listeners = listeners;
            Port = port;
        }

        /// <summary>
        /// Первый свободный порт из списка. Слушаем ОБА loopback-адреса
        /// отдельными сокетами: 127.0.0.1 (IPv4) и ::1 (IPv6).
        ///
        /// Почему не «dual-stack одним сокетом»: IPv6Only=false включает приём
        /// IPv4 только при привязке к IPv6Any (::). Сокет, привязанный именно
        /// к ::1, обслуживает лишь IPv6-loopback, поэтому браузер на
        /// http://127.0.0.1:PORT получал «отказано в подключении».
        /// Привязка к :: открыла бы порт на всех интерфейсах — это лишнее.
        /// </summary>
        public static LoopbackHttp Start()
        {
            foreach (var p in Ports)
            {
                var bound = new List<TcpListener>();
                try
                {
                    var v4 = new TcpListener(IPAddress.Loopback, p);
                    v4.Start();
                    bound.Add(v4);
                }
                catch { /* IPv4-loopback недоступен или порт занят */ }

                try
                {
                    var v6 = new TcpListener(new IPEndPoint(IPAddress.IPv6Loopback, p));
                    v6.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
                    v6.Start();
                    bound.Add(v6);
                }
                catch { /* системы без IPv6 — достаточно IPv4 */ }

                // Порт годится, только если поднялся хотя бы IPv4:
                // именно на 127.0.0.1 возвращаются площадки.
                if (bound.Count > 0 && bound[0].LocalEndpoint is IPEndPoint ep && ep.Address.Equals(IPAddress.Loopback))
                    return new LoopbackHttp(bound, p);

                foreach (var l in bound)
                {
                    try { l.Stop(); } catch { }
                }
            }
            throw new InvalidOperationException("Не удалось открыть локальный порт для авторизации");
        }

        /// Принять соединение с любого из loopback-сокетов.
        public async Task<Request> WaitAsync(CancellationToken ct)
        {
            var accepts = _listeners.Select(l => l.AcceptTcpClientAsync(ct).AsTask()).ToArray();
            var done = await Task.WhenAny(accepts);
            var client = await done;
            var firstLine = await ReadRequestLineAsync(client, ct);
            return new Request(client, firstLine);
        }

        /// Читает заголовки до "\r\n\r\n", возвращает первую строку запроса.
        private static async Task<string> ReadRequestLineAsync(TcpClient client, CancellationToken ct)
        {
            var ns = client.GetStream();
            var buf = new byte[8192];
            var text = new StringBuilder();
            while (text.Length < 65536)
            {
                int n = await ns.ReadAsync(buf.AsMemory(0, buf.Length), ct);
                if (n <= 0) break;
                text.Append(Encoding.ASCII.GetString(buf, 0, n));
                if (text.ToString().Contains("\r\n\r\n", StringComparison.Ordinal)) break;
            }
            var head = text.ToString();
            var eol = head.IndexOf("\r\n", StringComparison.Ordinal);
            return eol > 0 ? head[..eol] : head;
        }

        public void Dispose()
        {
            foreach (var l in _listeners)
            {
                try { l.Stop(); } catch { }
            }
        }

        public sealed class Request : IDisposable
        {
            private readonly TcpClient _client;
            public string Method { get; }
            public string Path { get; }
            private readonly Dictionary<string, string> _query = new(StringComparer.OrdinalIgnoreCase);

            public Request(TcpClient client, string firstLine)
            {
                _client = client;
                // "GET /oauth?access_token=..&state=.. HTTP/1.1"
                var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                Method = parts.Length > 0 ? parts[0].ToUpperInvariant() : "GET";
                var target = parts.Length > 1 ? parts[1] : "/";
                var q = target.IndexOf('?');
                Path = q >= 0 ? target[..q] : target;
                if (q >= 0)
                {
                    foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var eq = pair.IndexOf('=');
                        var k = Decode(eq < 0 ? pair : pair[..eq]);
                        var v = eq < 0 ? "" : Decode(pair[(eq + 1)..]);
                        if (k.Length > 0) _query[k] = v;
                    }
                }
            }

            private static string Decode(string s) =>
                Uri.UnescapeDataString(s.Replace("+", " "));

            public string? Query(string key) => _query.TryGetValue(key, out var v) ? v : null;

            public IEnumerable<string> QueryKeys() => _query.Keys;

            /// Ответ странице-мосту/браузеру. CORS: обязательны заголовки
            /// Allow-Origin и Allow-Private-Network — без них современный
            /// Chrome/Edge рубит запросы HTTPS-страницы моста к 127.0.0.1
            /// (Private Network Access), и токен до приложения не доходит:
            /// именно так ломался вход Twitch/VK.
            public async Task RespondAsync(int status, string? message, string? rawHtml = null)
            {
                try
                {
                    byte[] body;
                    if (rawHtml != null)
                    {
                        body = Encoding.UTF8.GetBytes(rawHtml);
                    }
                    else if (message != null)
                    {
                        var html =
                            "<!doctype html><meta charset=utf-8><title>YawaChatHub</title>" +
                            "<body style=\"margin:0;height:100vh;display:flex;align-items:center;justify-content:center;" +
                            "background:#0a0b13;color:#eceef6;font-family:Segoe UI,system-ui,sans-serif\">" +
                            "<div style=\"text-align:center;max-width:90vw\"><div style=\"width:44px;height:44px;margin:0 auto 14px;border-radius:12px;" +
                            "background:linear-gradient(135deg,#8b5cf6,#6366f1)\"></div>" +
                            $"<p style=\"font-size:15px\">{WebUtility.HtmlEncode(message)}</p>" +
                            "<p style=\"font-size:11px;color:#8b91a8\">Вкладку можно закрыть.</p></div></body>";
                        body = Encoding.UTF8.GetBytes(html);
                    }
                    else body = Array.Empty<byte>();

                    var sb = new StringBuilder();
                    sb.Append("HTTP/1.1 ").Append(status).Append(status == 204 ? " No Content\r\n" : " OK\r\n");
                    sb.Append("Access-Control-Allow-Origin: *\r\n");
                    sb.Append("Access-Control-Allow-Headers: *\r\n");
                    sb.Append("Access-Control-Allow-Methods: GET, OPTIONS\r\n");
                    sb.Append("Access-Control-Allow-Private-Network: true\r\n");
                    sb.Append("Connection: close\r\n");
                    if (body.Length > 0) sb.Append("Content-Type: text/html; charset=utf-8\r\n");
                    sb.Append("Content-Length: ").Append(body.Length).Append("\r\n\r\n");

                    var ns = _client.GetStream();
                    await ns.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()));
                    if (body.Length > 0) await ns.WriteAsync(body);
                }
                catch { }
                finally
                {
                    try { _client.Close(); } catch { }
                }
            }

            public void Dispose()
            {
                try { _client.Dispose(); } catch { }
            }
        }
    }
}

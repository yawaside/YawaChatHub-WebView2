using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YawaChatHub;

/* ============================================================
 * Коннекторы площадок (перенос desktop/electron/connectors).
 * Анонимное чтение чата, без OAuth там, где это возможно.
 * Все сообщения уходят в единую точку HostApi.PushChatMessage.
 *
 * Статусы канала:
 *   connecting — идёт подключение
 *   connected  — чат читается, эфир не подтверждён
 *   online     — подтверждён живой эфир (есть данные стрима)
 *   offline    — канала нет в эфире
 *   error      — сбой подключения
 * ============================================================ */

public sealed class ChannelSubInput
{
    public string platform { get; set; } = "";
    public string username { get; set; } = "";
    public string? token { get; set; }
    public string? currency { get; set; }
}

/// Сообщение чата, которое уходит в рендерер (форма = ChatMsg на фронте).
public sealed class ChatEvent
{
    public string platform { get; set; } = "";
    public string channel { get; set; } = "";
    public string author { get; set; } = "";
    public string color { get; set; } = "";
    public string text { get; set; } = "";
    public string kind { get; set; } = "chat";
    public string[]? badges { get; set; }
    public double? amount { get; set; }
    public string? currency { get; set; }
    public long ts { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public interface IPlatformConnector : IDisposable
{
    Task RunAsync(CancellationToken ct);
}

/* ------------------------- менеджер ------------------------- */

public sealed class ConnectorManager : IDisposable
{
    public delegate void OnChatMessage(ChatEvent msg);
    public delegate void OnChannelStatus(string platform, string username, string status, int viewers);

    private readonly OnChatMessage _onMsg;
    private readonly OnChannelStatus _onStatus;
    private readonly Dictionary<string, (IPlatformConnector conn, CancellationTokenSource cts)> _live = new();

    public ConnectorManager(OnChatMessage onMsg, OnChannelStatus onStatus)
    {
        _onMsg = onMsg;
        _onStatus = onStatus;
    }

    public void Connect(ChannelSubInput sub)
    {
        var key = $"{sub.platform}:{sub.username.ToLowerInvariant()}";
        Disconnect(sub.platform, sub.username);

        void emit(ChatEvent m) => _onMsg(m);
        void status(string s, int v) => _onStatus(sub.platform, sub.username, s, v);

        var cts = new CancellationTokenSource();

        // авто-переподключение с нарастающей паузой
        _ = Task.Run(async () =>
        {
            int attempt = 0;
            while (!cts.IsCancellationRequested)
            {
                IPlatformConnector? conn = null;
                try
                {
                    conn = sub.platform switch
                    {
                        "twitch" => new TwitchIrcConnector(sub.username, emit, status),
                        "kick" => new KickConnector(sub.username, emit, status),
                        "vkplay" => new VkVideoLiveConnector(sub.username, emit, status),
                        "youtube" => new YouTubeConnector(sub.username, emit, status),
                        "tiktok" => new TikTokConnector(sub.username, emit, status),
                        "donationalerts" => new DonationAlertsConnector(sub.username, sub.token ?? "", emit, status),
                        _ => throw new InvalidOperationException($"неизвестная площадка {sub.platform}"),
                    };
                    lock (_live) _live[key] = (conn, cts);
                    await conn.RunAsync(cts.Token);
                    attempt = 0;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[connector {key}] {ex.Message}");
                    status("error", 0);
                }
                finally { try { conn?.Dispose(); } catch { } }

                if (cts.IsCancellationRequested) return;
                var delay = Math.Min(30_000, 3_000 * Math.Max(1, ++attempt));
                try { await Task.Delay(delay, cts.Token); } catch { return; }
            }
        }, CancellationToken.None);
    }

    public void Disconnect(string platform, string username)
    {
        var key = $"{platform}:{username.ToLowerInvariant()}";
        (IPlatformConnector conn, CancellationTokenSource cts) item;
        lock (_live)
        {
            if (!_live.Remove(key, out item)) return;
        }
        try { item.cts.Cancel(); } catch { }
        try { item.conn.Dispose(); } catch { }
    }

    public void Dispose()
    {
        string[] keys;
        lock (_live) keys = _live.Keys.ToArray();
        foreach (var k in keys)
        {
            var parts = k.Split(':', 2);
            Disconnect(parts[0], parts.Length > 1 ? parts[1] : "");
        }
    }
}

/* ------------------------- хелперы ------------------------- */

internal static class Net
{
    public const string UA =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0.0.0 Safari/537.36";

    public static HttpClient Http()
    {
        var h = new HttpClient();
        h.DefaultRequestHeaders.UserAgent.ParseAdd(UA);
        h.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        h.Timeout = TimeSpan.FromSeconds(20);
        return h;
    }

    public static async Task<string?> WsRecv(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[32768];
        using var ms = new MemoryStream();
        WebSocketReceiveResult res;
        do
        {
            res = await ws.ReceiveAsync(buffer, ct);
            if (res.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, res.Count);
        } while (!res.EndOfMessage);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static Task WsSend(ClientWebSocket ws, string text, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);

    public static string StripHtml(string s) => Regex.Replace(s, "<[^>]+>", "");

    /// <summary>
    /// Универсальный токен картиночного смайла: [[e|URL|имя]].
    /// Рендерер превращает его в <img>, поэтому смайлы всех площадок
    /// (Twitch, Kick, VK) показываются картинками, а не текстом.
    /// </summary>
    public static string Emote(string url, string name) => $"[[e|{url}|{name}]]";

    /// Разбивка строки на code points (для корректной работы с индексами Twitch).
    public static List<string> ToCodePoints(string s)
    {
        var list = new List<string>(s.Length);
        for (int i = 0; i < s.Length;)
        {
            int len = char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
            list.Add(s.Substring(i, len));
            i += len;
        }
        return list;
    }

    /// Рекурсивный поиск свойства по имени в JSON-дереве (структуры площадок часто меняются).
    public static bool TryFind(JsonElement el, string name, out JsonElement found, int depth = 6)
    {
        found = default;
        if (depth < 0) return false;
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in el.EnumerateObject())
            {
                if (p.NameEquals(name)) { found = p.Value; return true; }
                if (TryFind(p.Value, name, out found, depth - 1)) return true;
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                if (TryFind(item, name, out found, depth - 1)) return true;
        }
        return false;
    }

    public static string? FindString(JsonElement el, string name) =>
        TryFind(el, name, out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;

    public static int FindInt(JsonElement el, string name) =>
        TryFind(el, name, out var f) && f.ValueKind == JsonValueKind.Number && f.TryGetInt32(out var v) ? v : 0;

    public static bool FindBool(JsonElement el, string name) =>
        TryFind(el, name, out var f) && f.ValueKind == JsonValueKind.True;
}

/* ========================== TWITCH (IRC, анонимное чтение) ========================== */

public sealed class TwitchIrcConnector : IPlatformConnector
{
    private readonly string _user;
    private readonly Action<ChatEvent> _emit;
    private readonly Action<string, int> _status;
    private TcpClient? _tcp;

    public TwitchIrcConnector(string user, Action<ChatEvent> emit, Action<string, int> status)
    {
        _user = user.ToLowerInvariant();
        _emit = emit;
        _status = status;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _status("connecting", 0);
        _tcp = new TcpClient();
        await _tcp.ConnectAsync("irc.chat.twitch.tv", 6697, ct);
        using var ssl = new SslStream(_tcp.GetStream());
        await ssl.AuthenticateAsClientAsync("irc.chat.twitch.tv");
        var reader = new StreamReader(ssl, Encoding.UTF8);
        var writer = new StreamWriter(ssl, new UTF8Encoding(false)) { AutoFlush = true };

        await writer.WriteLineAsync("CAP REQ :twitch.tv/tags twitch.tv/commands");
        await writer.WriteLineAsync("PASS oauth:justinfan");
        await writer.WriteLineAsync($"NICK justinfan{Random.Shared.Next(10000, 99999)}");
        await writer.WriteLineAsync($"JOIN #{_user}");
        _status("connected", 0);

        // реальный статус эфира и зрители
        _ = Task.Run(async () =>
        {
            using var http = Net.Http();
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var s = (await http.GetStringAsync($"https://decapi.me/twitch/viewercount/{_user}", ct)).Trim();
                    if (int.TryParse(s, out var v) && v >= 0) _status("online", v);
                    else _status("connected", 0);
                }
                catch { }
                try { await Task.Delay(45000, ct); } catch { return; }
            }
        }, CancellationToken.None);

        string? line;
        while (!ct.IsCancellationRequested && (line = await reader.ReadLineAsync(ct)) != null)
        {
            if (line.StartsWith("PING", StringComparison.Ordinal)) { await writer.WriteLineAsync("PONG :tmi.twitch.tv"); continue; }
            // разбор сообщения НИКОГДА не должен ронять соединение:
            // раньше сбой парсинга приводил к переподключению на каждом сообщении
            try
            {
                if (line.Contains(" PRIVMSG ", StringComparison.Ordinal)) HandlePrivmsg(line);
                else if (line.Contains(" USERNOTICE ", StringComparison.Ordinal)) HandleUserNotice(line);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[twitch parse] {ex.Message}");
            }
        }
    }

    private static Dictionary<string, string> Tags(string line)
    {
        var map = new Dictionary<string, string>();
        if (!line.StartsWith('@')) return map;
        var end = line.IndexOf(' ');
        if (end < 1) return map;
        foreach (var kv in line.Substring(1, end - 1).Split(';'))
        {
            var eq = kv.IndexOf('=');
            if (eq > 0) map[kv[..eq]] = kv[(eq + 1)..];
        }
        return map;
    }

    private static string ExtractText(string line, string marker)
    {
        var mi = line.IndexOf(marker, StringComparison.Ordinal);
        if (mi < 0) return "";
        var i = line.IndexOf(" :", mi, StringComparison.Ordinal);
        return i < 0 ? "" : line[(i + 2)..];
    }

    private void HandlePrivmsg(string line)
    {
        var tags = Tags(line);
        var text = ExtractText(line, "PRIVMSG");
        if (string.IsNullOrEmpty(text)) return;

        // Эмоуты Twitch: "25:0-4,12-23/354:30-35".
        // ВАЖНО: индексы указаны в code points (символах Юникода), а не в UTF-16.
        // Раньше это давало выход за границы на кириллице/эмодзи и роняло коннектор.
        if (tags.TryGetValue("emotes", out var em) && !string.IsNullOrEmpty(em))
        {
            var cps = Net.ToCodePoints(text);
            var spans = new List<(int start, int end, string id)>();
            foreach (var group in em.Split('/'))
                foreach (var item in group.Split(','))
                {
                    var colon = item.IndexOf(':');
                    if (colon <= 0) continue;
                    var id = item[..colon];
                    var range = item[(colon + 1)..];
                    var dash = range.IndexOf('-');
                    if (dash <= 0) continue;
                    if (int.TryParse(range[..dash], out var s) && int.TryParse(range[(dash + 1)..], out var e))
                        spans.Add((s, e, id));
                }

            spans.Sort((a, b) => b.start.CompareTo(a.start));
            foreach (var (s, e, id) in spans)
            {
                if (s < 0 || e >= cps.Count || e < s) continue;
                cps.RemoveRange(s, e - s + 1);
                cps.Insert(s, Net.Emote($"https://static-cdn.jtvnw.net/emoticons/v2/{id}/default/dark/2.0", "emote"));
            }
            text = string.Concat(cps);
        }

        var badges = ParseBadges(tags.TryGetValue("badges", out var b) ? b : "");
        _emit(new ChatEvent
        {
            platform = "twitch",
            channel = _user,
            author = tags.TryGetValue("display-name", out var dn) && dn.Length > 0 ? dn : _user,
            color = tags.TryGetValue("color", out var c) ? c : "",
            text = text,
            kind = "chat",
            badges = badges.Length > 0 ? badges : null,
        });
    }

    private void HandleUserNotice(string line)
    {
        var tags = Tags(line);
        if (!tags.TryGetValue("msg-id", out var id)) return;
        var sys = tags.TryGetValue("system-msg", out var sm) ? sm.Replace("\\s", " ") : "";
        var author = tags.TryGetValue("display-name", out var dn) ? dn : "";
        var ev = new ChatEvent { platform = "twitch", channel = _user, author = author, text = sys };
        switch (id)
        {
            case "sub": case "resub": ev.kind = "sub"; break;
            case "subgift": case "submysterygift": ev.kind = "gift"; break;
            case "raid": ev.kind = "raid"; break;
            case "bitsbadgetier": ev.kind = "bits"; break;
            default: return;
        }
        _emit(ev);
    }

    private static string[] ParseBadges(string s)
    {
        var list = new List<string>();
        foreach (var b in s.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = b.Split('/')[0];
            var map = name switch
            {
                "moderator" or "broadcaster" => "mod",
                "subscriber" => "sub",
                "vip" => "vip",
                "turbo" => "turbo",
                _ => "",
            };
            if (map.Length > 0) list.Add(map);
        }
        return list.ToArray();
    }

    public void Dispose() { try { _tcp?.Close(); } catch { } }
}

/* ========================== KICK (Pusher WebSocket) ========================== */

public sealed class KickConnector : IPlatformConnector
{
    private readonly string _user;
    private readonly Action<ChatEvent> _emit;
    private readonly Action<string, int> _status;
    private ClientWebSocket? _ws;

    public KickConnector(string user, Action<ChatEvent> emit, Action<string, int> status)
    {
        _user = user.ToLowerInvariant();
        _emit = emit;
        _status = status;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _status("connecting", 0);
        using var http = Net.Http();
        var chJson = await http.GetStringAsync($"https://kick.com/api/v2/channels/{_user}", ct);
        using var ch = JsonDocument.Parse(chJson);
        if (!ch.RootElement.TryGetProperty("chatroom", out var room))
            throw new InvalidOperationException($"Kick: канал «{_user}» не найден");
        var chatroomId = room.GetProperty("id").GetInt32();
        var isLive = ch.RootElement.TryGetProperty("livestream", out var ls) && ls.ValueKind == JsonValueKind.Object;
        var viewers = isLive && ls.TryGetProperty("viewer_count", out var vc) ? vc.GetInt32() : 0;

        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(
            new Uri("wss://ws-us2.pusher.com/app/eb1d5f283081a78b932c?protocol=7&client=js&version=7.6.0&flash=false"), ct);
        await Net.WsSend(_ws, JsonSerializer.Serialize(new
        {
            @event = "pusher:subscribe",
            data = new { auth = "", channel = $"chatrooms.{chatroomId}.v2" },
        }), ct);

        _status(isLive ? "online" : "connected", viewers);

        // периодическая проверка эфира
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(60000, ct); } catch { return; }
                try
                {
                    var j = await http.GetStringAsync($"https://kick.com/api/v2/channels/{_user}", ct);
                    using var d = JsonDocument.Parse(j);
                    var live = d.RootElement.TryGetProperty("livestream", out var l) && l.ValueKind == JsonValueKind.Object;
                    var v = live && l.TryGetProperty("viewer_count", out var x) ? x.GetInt32() : 0;
                    _status(live ? "online" : "connected", v);
                }
                catch { }
            }
        }, CancellationToken.None);

        while (!ct.IsCancellationRequested)
        {
            var text = await Net.WsRecv(_ws, ct);
            if (text == null) throw new InvalidOperationException("Kick: соединение закрыто");
            using var doc = JsonDocument.Parse(text);
            var evName = doc.RootElement.TryGetProperty("event", out var e) ? e.GetString() : "";
            if (evName == "pusher:ping")
            {
                await Net.WsSend(_ws, "{\"event\":\"pusher:pong\",\"data\":{}}", ct);
                continue;
            }
            if (!string.Equals(evName, "App\\Events\\ChatMessageEvent", StringComparison.Ordinal)) continue;

            var payload = doc.RootElement.GetProperty("data").GetString();
            if (string.IsNullOrEmpty(payload)) continue;
            using var inner = JsonDocument.Parse(payload);
            var dataEl = inner.RootElement;

            if (dataEl.TryGetProperty("type", out var tp))
            {
                var t = tp.GetString();
                if (t != "message" && t != "reply") continue;
            }
            var content = dataEl.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
            // смайлы Kick: [emote:123456:KEKW] → картинка с CDN
            content = Regex.Replace(content, @"\[emote:(\d+):([\w]+)\]",
                m => Net.Emote($"https://files.kick.com/emotes/{m.Groups[1].Value}/fullsize", m.Groups[2].Value));
            if (content.Length == 0) continue;

            var sender = dataEl.GetProperty("sender");
            var author = sender.TryGetProperty("username", out var un) ? un.GetString() ?? "" : "";
            var color = "";
            var badges = new List<string>();
            if (sender.TryGetProperty("identity", out var idn))
            {
                if (idn.TryGetProperty("color", out var cl)) color = cl.GetString() ?? "";
                if (idn.TryGetProperty("badges", out var bArr) && bArr.ValueKind == JsonValueKind.Array)
                    foreach (var b in bArr.EnumerateArray())
                    {
                        var t = b.TryGetProperty("type", out var bt) ? bt.GetString() : "";
                        var map = t switch
                        {
                            "moderator" or "broadcaster" => "mod",
                            "subscriber" => "sub",
                            "vip" => "vip",
                            _ => "",
                        };
                        if (map.Length > 0) badges.Add(map);
                    }
            }
            _emit(new ChatEvent
            {
                platform = "kick",
                channel = _user,
                author = author,
                color = color,
                text = content,
                kind = "chat",
                badges = badges.Count > 0 ? badges.ToArray() : null,
            });
        }
    }

    public void Dispose() { try { _ws?.Abort(); } catch { } }
}

/* ============ VK ВИДЕО LIVE (Centrifugo v2, публичный чат) ============
 * Протокол (реверс официального веб-клиента live.vkvideo.ru):
 *   1. GET https://api.live.vkvideo.ru/v1/blog/{channel}  → publicWebSocketChannel
 *   2. GET https://api.live.vkvideo.ru/v1/ws/connect      → анонимный токен
 *   3. wss://pubsub.live.vkvideo.ru/connection/websocket?cf_protocol_version=v2
 *      с обязательным заголовком Origin: https://live.vkvideo.ru
 *   4. {"connect":{"token":..,"name":"js"},"id":1}
 *      {"subscribe":{"channel":"public-chat:<ch>"},"id":2}
 *      {"subscribe":{"channel":"channel-info:<ch>"},"id":3}
 *   5. keep-alive: сервер шлёт "{}", клиент обязан ответить "{}"
 * Сообщение: push.pub.data.type == "message", блоки текста лежат в data.data[]
 * ==================================================================== */

public sealed class VkVideoLiveConnector : IPlatformConnector
{
    private const string ApiBase = "https://api.live.vkvideo.ru/v1";
    private const string WsUrl = "wss://pubsub.live.vkvideo.ru/connection/websocket?cf_protocol_version=v2";
    private const string Origin = "https://live.vkvideo.ru";

    private readonly string _user;
    private readonly Action<ChatEvent> _emit;
    private readonly Action<string, int> _status;
    private ClientWebSocket? _ws;

    public VkVideoLiveConnector(string user, Action<ChatEvent> emit, Action<string, int> status)
    {
        _user = user.Trim().TrimStart('@').ToLowerInvariant();
        _emit = emit;
        _status = status;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _status("connecting", 0);

        using var http = Net.Http();
        http.DefaultRequestHeaders.Add("Origin", Origin);
        http.DefaultRequestHeaders.Referrer = new Uri($"{Origin}/{_user}");

        /* 1) информация о канале */
        var blogResp = await http.GetAsync($"{ApiBase}/blog/{_user}", ct);
        if (!blogResp.IsSuccessStatusCode)
            throw new InvalidOperationException($"VK: канал «{_user}» не найден ({(int)blogResp.StatusCode})");
        var blogJson = await blogResp.Content.ReadAsStringAsync(ct);

        using var blogDoc = JsonDocument.Parse(blogJson);
        var blogRoot = blogDoc.RootElement;

        var wsChannel = Net.FindString(blogRoot, "publicWebSocketChannel");
        if (string.IsNullOrEmpty(wsChannel))
            throw new InvalidOperationException("VK: не удалось получить канал чата (publicWebSocketChannel)");

        bool live = Net.FindBool(blogRoot, "isOnline");
        int viewers = Net.FindInt(blogRoot, "viewers");

        /* 2) анонимный websocket-токен */
        string wsToken = "";
        try
        {
            var tokenJson = await http.GetStringAsync($"{ApiBase}/ws/connect", ct);
            using var tokenDoc = JsonDocument.Parse(tokenJson);
            wsToken = Net.FindString(tokenDoc.RootElement, "token") ?? "";
        }
        catch { /* публичный чат читается и с пустым токеном */ }

        /* 3) Centrifugo v2 */
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Origin", Origin);
        _ws.Options.SetRequestHeader("User-Agent", Net.UA);
        await _ws.ConnectAsync(new Uri(WsUrl), ct);

        await Net.WsSend(_ws, JsonSerializer.Serialize(new { connect = new { token = wsToken, name = "js" }, id = 1 }), ct);
        var hello = await Net.WsRecv(_ws, ct);
        if (hello == null) throw new InvalidOperationException("VK: сервер закрыл соединение при подключении");
        if (hello.Contains("\"error\"", StringComparison.Ordinal))
            throw new InvalidOperationException("VK: отказ авторизации сокета — " + hello);

        // канал уже может содержать префикс — не дублируем его
        var chatCh = wsChannel.Contains(':') ? wsChannel : $"public-chat:{wsChannel}";
        var infoCh = wsChannel.Contains(':') ? wsChannel : $"channel-info:{wsChannel}";

        await Net.WsSend(_ws, JsonSerializer.Serialize(new { subscribe = new { channel = chatCh }, id = 2 }), ct);
        await Net.WsSend(_ws, JsonSerializer.Serialize(new { subscribe = new { channel = infoCh }, id = 3 }), ct);

        _status(live ? "online" : "connected", live ? viewers : 0);

        /* 4) приём сообщений; keep-alive: на "{}" отвечаем "{}" */
        while (!ct.IsCancellationRequested)
        {
            var frame = await Net.WsRecv(_ws, ct);
            if (frame == null) throw new InvalidOperationException("VK: соединение с чатом закрыто");

            // Centrifugo шлёт JSON-объекты, разделённые переводом строки
            foreach (var line in frame.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var s = line.Trim();
                if (s.Length == 0) continue;
                if (s == "{}")
                {
                    await Net.WsSend(_ws, "{}", ct);   // pong обязателен, иначе сервер рвёт связь
                    continue;
                }
                try { HandleFrame(s); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[vk parse] {ex.Message} :: {s}"); }
            }
        }
    }

    private void HandleFrame(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // ответ на subscribe с ошибкой — сразу видно причину
        if (root.TryGetProperty("error", out var err))
        {
            var msg = Net.FindString(err, "message") ?? err.GetRawText();
            System.Diagnostics.Debug.WriteLine($"[vk] ошибка подписки: {msg}");
            return;
        }

        if (!root.TryGetProperty("push", out var push)) return;
        if (!push.TryGetProperty("pub", out var pub)) return;
        if (!pub.TryGetProperty("data", out var data)) return;

        var type = data.TryGetProperty("type", out var t) ? t.GetString() : "";

        // страховка: тип не распознан, но структура похожа на сообщение чата
        if (string.IsNullOrEmpty(type) && data.TryGetProperty("data", out _))
            type = "message";

        switch (type)
        {
            case "message":
                EmitChat(data);
                break;

            case "stream_online_status":
            {
                // единственный достоверный источник статуса эфира
                var online = data.TryGetProperty("isOnline", out var io) && io.ValueKind == JsonValueKind.True;
                var v = data.TryGetProperty("viewers", out var vw) && vw.TryGetInt32(out var vi) ? vi : 0;
                _status(online ? "online" : "connected", online ? v : 0);
                break;
            }

            case "stream_start":
                _status("online", 0);
                break;

            case "stream_end":
                _status("connected", 0);
                break;

            case "cp_reward_demand":
            {
                var inner = data.TryGetProperty("data", out var rd) ? rd : data;
                var nick = Net.FindString(inner, "nick") ?? Net.FindString(inner, "displayName") ?? "";
                var rewardName = Net.TryFind(inner, "reward", out var rw) ? Net.FindString(rw, "name") ?? "награда" : "награда";
                _emit(new ChatEvent
                {
                    platform = "vkplay",
                    channel = _user,
                    author = nick,
                    text = $"активировал награду «{rewardName}»",
                    kind = "reward",
                });
                break;
            }

            case "actions_journal_new_event":
            {
                var inner = data.TryGetProperty("data", out var jd) ? jd : data;
                var evType = inner.TryGetProperty("type", out var jt) ? jt.GetString() : "";
                if (evType is "following" or "actions_journal_follower")
                {
                    var nick = Net.FindString(inner, "displayName") ?? Net.FindString(inner, "nick") ?? "";
                    _emit(new ChatEvent
                    {
                        platform = "vkplay",
                        channel = _user,
                        author = nick,
                        text = "новый подписчик канала",
                        kind = "sub",
                    });
                }
                break;
            }
        }
    }

    private void EmitChat(JsonElement data)
    {
        // тело сообщения: обычно data.data, но встречается и плоская форма
        var msg = data.TryGetProperty("data", out var inner) ? inner : data;

        // автор: author → user → рекурсивный поиск по дереву
        var author = "";
        if (msg.TryGetProperty("author", out var a) || msg.TryGetProperty("user", out a))
            author = (a.TryGetProperty("displayName", out var dn) ? dn.GetString() : null)
                     ?? (a.TryGetProperty("nick", out var nk) ? nk.GetString() : null)
                     ?? "";
        if (string.IsNullOrEmpty(author))
            author = Net.FindString(msg, "displayName") ?? Net.FindString(msg, "nick") ?? "";

        // бейджи: владелец / модератор
        var badges = new List<string>();
        if (msg.TryGetProperty("author", out var au))
        {
            if (au.TryGetProperty("isOwner", out var ow) && ow.ValueKind == JsonValueKind.True) badges.Add("mod");
            else if ((au.TryGetProperty("isChatModerator", out var cm) && cm.ValueKind == JsonValueKind.True) ||
                     (au.TryGetProperty("isChannelModerator", out var chm) && chm.ValueKind == JsonValueKind.True))
                badges.Add("mod");
            if (au.TryGetProperty("badges", out var bd) && bd.ValueKind == JsonValueKind.Array && bd.GetArrayLength() > 0)
                badges.Add("sub");
        }

        // текст: массив блоков (text / smile / mention / link)
        var sb = new StringBuilder();
        if (!msg.TryGetProperty("data", out var blocks) || blocks.ValueKind != JsonValueKind.Array)
            Net.TryFind(msg, "data", out blocks);   // блоки могут лежать глубже

        if (blocks.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in blocks.EnumerateArray())
            {
                var bt = b.TryGetProperty("type", out var bty) ? bty.GetString() : "";
                switch (bt)
                {
                    case "text":
                    {
                        // content — строка с JSON-массивом: "[\"привет\",\"unstyled\",[]]"
                        var raw = b.TryGetProperty("content", out var cEl) ? cEl.GetString() ?? "" : "";
                        sb.Append(ParseTextBlock(raw));
                        break;
                    }
                    case "smile":
                    {
                        // смайлы VK приходят с готовыми URL — показываем картинкой
                        var url = (b.TryGetProperty("largeUrl", out var lu2) ? lu2.GetString() : null)
                                  ?? (b.TryGetProperty("mediumUrl", out var mu) ? mu.GetString() : null)
                                  ?? (b.TryGetProperty("smallUrl", out var su) ? su.GetString() : null);
                        var nm = b.TryGetProperty("name", out var sn) ? sn.GetString() ?? "smile" : "smile";
                        sb.Append(string.IsNullOrEmpty(url) ? $":{nm}:" : Net.Emote(url, nm));
                        break;
                    }
                    case "mention":
                        sb.Append('@')
                          .Append((b.TryGetProperty("displayName", out var mdn) ? mdn.GetString() : null)
                                  ?? (b.TryGetProperty("nick", out var mn) ? mn.GetString() : "") ?? "")
                          .Append(' ');
                        break;
                    case "link":
                        sb.Append(b.TryGetProperty("content", out var lc) ? lc.GetString() : b.TryGetProperty("url", out var lu) ? lu.GetString() : "");
                        break;
                }
            }
        }

        var text = sb.ToString().Trim();
        // последний резерв: простое текстовое поле
        if (text.Length == 0) text = (Net.FindString(msg, "text") ?? "").Trim();
        if (text.Length == 0) return;

        _emit(new ChatEvent
        {
            platform = "vkplay",
            channel = _user,
            author = author,
            text = text,
            kind = "chat",
            badges = badges.Count > 0 ? badges.ToArray() : null,
        });
    }

    /// Блок текста приходит как строка с JSON-массивом ["текст","unstyled",[]]
    private static string ParseTextBlock(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        if (!raw.TrimStart().StartsWith('[')) return raw;
        try
        {
            using var d = JsonDocument.Parse(raw);
            if (d.RootElement.ValueKind == JsonValueKind.Array && d.RootElement.GetArrayLength() > 0)
            {
                var first = d.RootElement[0];
                if (first.ValueKind == JsonValueKind.String) return first.GetString() ?? "";
            }
        }
        catch { }
        return raw;
    }

    public void Dispose() { try { _ws?.Abort(); } catch { } }
}

/* ============== YOUTUBE LIVE (innertube get_live_chat, без API-ключа) ============== */

public sealed class YouTubeConnector : IPlatformConnector
{
    private readonly string _user;
    private readonly Action<ChatEvent> _emit;
    private readonly Action<string, int> _status;

    public YouTubeConnector(string user, Action<ChatEvent> emit, Action<string, int> status)
    {
        _user = user.Trim().TrimStart('@');
        _emit = emit;
        _status = status;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var http = Net.Http();
        while (!ct.IsCancellationRequested)
        {
            _status("connecting", 0);
            string? liveHtml = null;
            foreach (var url in new[]
            {
                $"https://www.youtube.com/@{_user}/live",
                $"https://www.youtube.com/channel/{_user}/live",
                $"https://www.youtube.com/user/{_user}/live",
            })
            {
                try
                {
                    var h = await http.GetStringAsync(url, ct);
                    if (h.Contains("liveChatRenderer") || h.Contains("\"videoId\":\"")) { liveHtml = h; break; }
                }
                catch { }
            }

            string? cont = liveHtml == null ? null : ExtractAfter(liveHtml, "liveChatRenderer", "\"continuation\":\"", "\"");
            if (liveHtml == null || cont == null)
            {
                _status("connected", 0);   // канал есть, но эфира нет
                try { await Task.Delay(TimeSpan.FromMinutes(2), ct); } catch { return; }
                continue;
            }

            var apiKey = Extract(liveHtml, "\"INNERTUBE_API_KEY\":\"", "\"") ?? throw new InvalidOperationException("YouTube: innertube key не найден");
            var clientVer = Extract(liveHtml, "\"clientVersion\":\"", "\"") ?? "2.20240620.01.00";
            _status("online", 0);

            var errorStreak = 0;
            while (!ct.IsCancellationRequested)
            {
                string resp;
                try
                {
                    var body = JsonSerializer.Serialize(new
                    {
                        context = new { client = new { clientName = "WEB", clientVersion = clientVer } },
                        continuation = cont,
                    });
                    using var content = new StringContent(body, Encoding.UTF8, "application/json");
                    var r = await http.PostAsync($"https://www.youtube.com/youtubei/v1/live_chat/get_live_chat?key={apiKey}", content, ct);
                    resp = await r.Content.ReadAsStringAsync(ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    if (++errorStreak > 5) break;   // вернёмся к поиску эфира
                    try { await Task.Delay(5000, ct); } catch { return; }
                    continue;
                }
                errorStreak = 0;
                var waitMs = 1500;

                using var doc = JsonDocument.Parse(resp);
                if (doc.RootElement.TryGetProperty("continuationContents", out var cc) &&
                    cc.TryGetProperty("liveChatContinuation", out var lcc))
                {
                    if (lcc.TryGetProperty("continuations", out var conts) && conts.GetArrayLength() > 0)
                    {
                        var c0 = conts[0];
                        if (c0.TryGetProperty("timedContinuationData", out var tcd))
                        {
                            cont = tcd.GetProperty("continuation").GetString() ?? cont;
                            if (tcd.TryGetProperty("timeoutMs", out var tm)) waitMs = tm.GetInt32();
                        }
                        else if (c0.TryGetProperty("invalidationContinuationData", out var icd))
                            cont = icd.GetProperty("continuation").GetString() ?? cont;
                    }

                    if (lcc.TryGetProperty("actions", out var actions))
                        foreach (var a in actions.EnumerateArray())
                        {
                            if (!a.TryGetProperty("addChatItemAction", out var add)) continue;
                            if (!add.TryGetProperty("item", out var item)) continue;
                            if (!item.TryGetProperty("liveChatTextMessageRenderer", out var m)) continue;

                            var sb = new StringBuilder();
                            if (m.TryGetProperty("message", out var msg) && msg.TryGetProperty("runs", out var runs))
                                foreach (var r2 in runs.EnumerateArray())
                                {
                                    if (r2.TryGetProperty("text", out var tEl)) sb.Append(tEl.GetString());
                                    else if (r2.TryGetProperty("emoji", out var emo) && emo.TryGetProperty("shortcuts", out var sc) && sc.GetArrayLength() > 0)
                                        sb.Append(sc[0].GetString());
                                }
                            var text = sb.ToString();
                            if (text.Length == 0) continue;
                            var author = m.TryGetProperty("authorName", out var an) && an.TryGetProperty("simpleText", out var st) ? st.GetString() ?? "" : "";
                            var badges = new List<string>();
                            if (m.TryGetProperty("authorBadges", out var ab))
                            {
                                var abRaw = ab.GetRawText();
                                if (abRaw.Contains("moderator", StringComparison.OrdinalIgnoreCase) || abRaw.Contains("owner", StringComparison.OrdinalIgnoreCase)) badges.Add("mod");
                                if (abRaw.Contains("member", StringComparison.OrdinalIgnoreCase)) badges.Add("sub");
                            }
                            _emit(new ChatEvent
                            {
                                platform = "youtube",
                                channel = _user,
                                author = author,
                                text = text,
                                kind = "chat",
                                badges = badges.Count > 0 ? badges.ToArray() : null,
                            });
                        }
                }
                try { await Task.Delay(Math.Clamp(waitMs, 800, 10_000), ct); } catch { return; }
            }
        }
    }

    private static string? Extract(string src, string from, string to)
    {
        var i = src.IndexOf(from, StringComparison.Ordinal);
        if (i < 0) return null;
        i += from.Length;
        var j = src.IndexOf(to, i, StringComparison.Ordinal);
        return j < 0 ? null : src[i..j];
    }

    private static string? ExtractAfter(string src, string marker, string from, string to)
    {
        var k = src.IndexOf(marker, StringComparison.Ordinal);
        return k < 0 ? null : Extract(src[k..], from, to);
    }

    public void Dispose() { }
}

/* ================== DONATIONALERTS (Centrifugo по секретному токену) ================== */

public sealed class DonationAlertsConnector : IPlatformConnector
{
    private readonly string _user;
    private readonly string _token;
    private readonly Action<ChatEvent> _emit;
    private readonly Action<string, int> _status;
    private ClientWebSocket? _ws;

    public DonationAlertsConnector(string user, string token, Action<ChatEvent> emit, Action<string, int> status)
    {
        _user = user;
        _token = token;
        _emit = emit;
        _status = status;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_token)) throw new InvalidOperationException("нужен секретный токен DonationAlerts");
        _status("connecting", 0);

        using var http = Net.Http();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        var userResp = await http.GetStringAsync("https://www.donationalerts.com/api/v1/user/oauth", ct);
        using var userDoc = JsonDocument.Parse(userResp);
        var data = userDoc.RootElement.GetProperty("data");
        var uid = data.GetProperty("id").GetInt64().ToString();
        var socketToken = data.TryGetProperty("socket_connection_token", out var sct) ? sct.GetString() ?? "" : "";
        if (socketToken.Length == 0) throw new InvalidOperationException("DonationAlerts: неверный токен");

        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri("wss://centrifugo.donationalerts.com/connection/websocket"), ct);
        await Net.WsSend(_ws, JsonSerializer.Serialize(new { @params = new { token = socketToken }, method = 1, id = 1 }), ct);

        string? clientId = null;
        var handshake = await Net.WsRecv(_ws, ct) ?? throw new InvalidOperationException("DonationAlerts: нет ответа");
        using (var hs = JsonDocument.Parse(handshake))
            if (hs.RootElement.TryGetProperty("result", out var res) && res.TryGetProperty("client", out var cl))
                clientId = cl.GetString();
        if (string.IsNullOrEmpty(clientId)) throw new InvalidOperationException("DonationAlerts: client id не получен");

        var subBody = JsonSerializer.Serialize(new { channels = new[] { $"$alerts:donation_{uid}" }, client = clientId });
        using var subContent = new StringContent(subBody, Encoding.UTF8, "application/json");
        var subResp = await http.PostAsync("https://www.donationalerts.com/api/v1/centrifuge/subscribe", subContent, ct);
        subResp.EnsureSuccessStatusCode();
        _status("connected", 0);

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Net.WsSend(_ws!, "{\"method\":7,\"id\":7}", ct); } catch { return; }
                try { await Task.Delay(25000, ct); } catch { return; }
            }
        }, CancellationToken.None);

        while (!ct.IsCancellationRequested)
        {
            var text = await Net.WsRecv(_ws, ct);
            if (text == null) throw new InvalidOperationException("DonationAlerts: соединение закрыто");
            if (!text.Contains("username", StringComparison.OrdinalIgnoreCase)) continue;

            var mUser = Regex.Match(text, "\"username\"\\s*:\\s*\"(?<v>[^\"]*)\"");
            var mAmount = Regex.Match(text, "\"amount\"\\s*:\\s*(?<v>[0-9.,]+)");
            var mCur = Regex.Match(text, "\"currency\"\\s*:\\s*\"(?<v>[A-Z]{3})\"");
            var mMsg = Regex.Match(text, "\"message\"\\s*:\\s*\"(?<v>((?:[^\"\\\\])|\\\\.)*)\"");
            if (!mUser.Success) continue;

            var cur = mCur.Success ? mCur.Groups["v"].Value : "RUB";
            var symbol = cur == "USD" ? "$" : cur == "EUR" ? "€" : "₽";
            _emit(new ChatEvent
            {
                platform = "donationalerts",
                channel = _user,
                author = mUser.Groups["v"].Value,
                text = mMsg.Success ? Net.StripHtml(mMsg.Groups["v"].Value.Replace("\\\"", "\"").Replace("\\n", " ")) : "",
                kind = "donate",
                amount = mAmount.Success && double.TryParse(mAmount.Groups["v"].Value.Replace(',', '.'),
                    System.Globalization.CultureInfo.InvariantCulture, out var am) ? am : null,
                currency = symbol,
            });
        }
    }

    public void Dispose() { try { _ws?.Abort(); } catch { } }
}

/* ============ TIKTOK LIVE (webcast chat fetch — экспериментально) ============ */

public sealed class TikTokConnector : IPlatformConnector
{
    private readonly string _user;
    private readonly Action<ChatEvent> _emit;
    private readonly Action<string, int> _status;

    public TikTokConnector(string user, Action<ChatEvent> emit, Action<string, int> status)
    {
        _user = user.Trim().TrimStart('@');
        _emit = emit;
        _status = status;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _status("connecting", 0);
        using var http = Net.Http();

        string roomId = "";
        while (!ct.IsCancellationRequested && roomId.Length == 0)
        {
            try
            {
                var html = await http.GetStringAsync($"https://www.tiktok.com/@{_user}/live", ct);
                var room = Regex.Match(html, "\"roomId\"\\s*:\\s*\"?(\\d+)\"?");
                if (room.Success) { roomId = room.Groups[1].Value; break; }
            }
            catch { }
            _status("connected", 0);   // канал есть, эфира нет
            try { await Task.Delay(TimeSpan.FromMinutes(2), ct); } catch { return; }
        }
        if (roomId.Length == 0) return;
        _status("online", 0);

        var cursor = "";
        var fails = 0;
        while (!ct.IsCancellationRequested)
        {
            string json;
            try
            {
                json = await http.GetStringAsync(
                    $"https://webcast.tiktok.com/webcast/chat/fetch/?aid=1988&room_id={roomId}&cursor={cursor}", ct);
            }
            catch
            {
                if (++fails > 4) throw new InvalidOperationException("TikTok: webcast недоступен (нужны cookies)");
                try { await Task.Delay(5000, ct); } catch { return; }
                continue;
            }
            fails = 0;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var d))
            {
                if (d.TryGetProperty("next_cursor", out var nc)) cursor = nc.GetString() ?? cursor;
                else if (d.TryGetProperty("cursor", out var cc)) cursor = cc.GetString() ?? cursor;
                if (d.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
                    foreach (var m in msgs.EnumerateArray())
                    {
                        var method = m.TryGetProperty("common", out var cm) && cm.TryGetProperty("method", out var md) ? md.GetString() : "";
                        if (method != "WebcastChatMessage") continue;
                        var nick = m.TryGetProperty("user", out var u) && u.TryGetProperty("nickname", out var nn) ? nn.GetString() ?? "" : "";
                        var content = m.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                        if (content.Length == 0) continue;
                        _emit(new ChatEvent { platform = "tiktok", channel = _user, author = nick, text = content, kind = "chat" });
                    }
            }
            try { await Task.Delay(2500, ct); } catch { return; }
        }
    }

    public void Dispose() { }
}

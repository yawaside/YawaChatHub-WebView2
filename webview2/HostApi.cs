using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.WinForms;

namespace YawaChatHub;

/// <summary>
/// JSON-RPC мост рендерер ↔ хост. Заменяет electron/ipc: те же методы,
/// что вызывает src/lib/bridge.ts — settings.*, window.*, tts.*,
/// overlay.*, widget.info, hotkeys.apply, clipboard.set, shell.openExternal.
/// Настройки живут в %LOCALAPPDATA%\YawaChatHub\settings.json (как раньше у Electron).
/// </summary>
public sealed class HostApi : IDisposable
{
    private readonly MainWindow _win;
    private readonly WebView2 _web;
    private readonly SapiTts _tts = new();
    private readonly EdgeTts _edge = new();
    private readonly WidgetServer _widget = new();
    private readonly ConnectorManager _connectors;
    private OverlayWindow? _overlay;
    private JsonNode? _overlayConfig;     // последний конфиг — применяется к новому окну
    private readonly string _settingsPath = Path.Combine(Program.AppDir, "settings.json");
    private JsonObject _settings = new();

    /// Создаёт окно оверлея при первом обращении и сразу применяет сохранённые настройки.
    private OverlayWindow EnsureOverlay()
    {
        if (_overlay == null || _overlay.IsDisposed)
        {
            _overlay = new OverlayWindow(this);
            // перетащили окно → сохраняем posX/posY в settings.json (и в конфиг оверлея)
            _overlay.PositionChanged += (x, y) =>
            {
                if (_overlayConfig is JsonObject cfgObj)
                {
                    cfgObj["posX"] = x;
                    cfgObj["posY"] = y;
                }
                if (_settings["overlay"] is JsonObject savedObj)
                {
                    savedObj["posX"] = x;
                    savedObj["posY"] = y;
                    SaveSettings();
                }
                _win.SendEvent("overlay.moved", new { x, y });
            };
            var cfg = _overlayConfig
                ?? (_settings.TryGetPropertyValue("overlay", out var saved) ? saved?.DeepClone() : null);
            if (cfg != null) _overlay.Apply(cfg);
        }
        return _overlay;
    }

    public HostApi(MainWindow win, WebView2 web)
    {
        _win = win;
        _web = web;
        LoadSettings();
        // VK отдаёт токен только во фрагменте blank.html: читаем адрес из буфера
        // обмена, причём строго в UI-потоке — Clipboard требует STA.
        AppAuth.ClipboardReader = () =>
        {
            try
            {
                return _win.IsHandleCreated
                    ? _win.Invoke(() => Clipboard.ContainsText() ? Clipboard.GetText() : null)
                    : null;
            }
            catch { return null; }
        };
        // Twitch: device-код кладём в буфер — пользователю остаётся только вставить
        AppAuth.ClipboardWriter = text =>
        {
            try { _win.BeginInvoke(() => Clipboard.SetText(text)); } catch { }
        };
        // Токен бота включает права модерации и отправки в Twitch-коннекторе
        ConnectorManager.BotToken = _settings["botToken:twitch"]?.GetValue<string>() ?? "";
        ConnectorManager.BotLogin = (_settings["botAuth"] is JsonObject ba
            ? ba["twitch"]?.GetValue<string>() : null) ?? "";
        _edge.Warmup();   // прогрев соединения Edge TTS — первая фраза без задержки
        _connectors = new ConnectorManager(
            msg => PushChatMessage(msg),
            (platform, username, status, viewers) =>
                _win.SendEvent("channel.status", new { platform, username, status, viewers }));
    }

    /// <param name="reply">
    /// Куда отправить ответ. По умолчанию — главное окно; окно оверлея
    /// передаёт свой Reply, чтобы получать собственные настройки.
    /// </param>
    public async Task DispatchAsync(
        int id,
        string method,
        object?[] args,
        Action<int, bool, object?, string?>? reply = null)
    {
        reply ??= (i, ok, res, err) => _win.Reply(i, ok, res, err);
        try
        {
            object? result = await Route(method, args);
            if (id != 0) reply(id, true, result, null);
        }
        catch (Exception ex)
        {
            if (id != 0) reply(id, false, null, ex.Message);
        }
    }

    private async Task<object?> Route(string method, object?[] a)
    {
        switch (method)
        {
            /* ---------- настройки ---------- */
            case "settings.get":
            {
                var key = Str(a, 0) ?? "";
                return _settings.TryGetPropertyValue(key, out var node) ? node?.DeepClone() : null;
            }
            case "settings.set":
            {
                var key = Str(a, 0) ?? "";
                _settings[key] = a.Length > 1 ? ToNode(a[1]) : null;
                SaveSettings();
                if (key == "closeToTray") _win.SetCloseToTray(a.Length > 1 && Bool(a, 1));
                return null;
            }

            /* ---------- окно ---------- */
            case "window.minimize": _win.BeginInvoke(() => _win.WindowState = FormWindowState.Minimized); return null;
            case "window.toggleMaximize": _win.BeginInvoke(_win.ToggleMaximize); return null;
            case "window.isMaximized": return _win.WindowState == FormWindowState.Maximized;
            case "window.close":
            {
                bool toTray = Bool(a, 0);
                _win.BeginInvoke(() =>
                {
                    if (toTray) _win.Hide();
                    else { _win.SetCloseToTray(false); _win.Close(); }
                });
                return null;
            }
            case "window.drag": _win.BeginInvoke(_win.BeginDrag); return null;

            /* ---------- озвучка: Edge TTS (нейро, только русский) или системный SAPI ---------- */
            case "tts.voices":
                // список доступных голосов для панели «Озвучка»
                return new
                {
                    edge = EdgeTts.RussianVoices.Select(v => new { id = v.Id, title = v.Title, gender = v.Gender }),
                    sapi = SapiTts.ListRussianVoices(),
                };

            case "tts.speak":
            {
                var text = Str(a, 0) ?? "";
                var voice = Str(a, 1) ?? "";
                var rate = a.Length > 2 ? Int(a, 2) : 0;      // -10..10 (SAPI) / проценты (Edge)
                var volume = a.Length > 3 ? Int(a, 3) : 100;  // 0..100
                var engine = a.Length > 4 ? Str(a, 4) ?? "edge" : "edge";

                if (engine == "sapi") await _tts.SpeakAsync(text, voice, rate, volume);
                else await _edge.SpeakAsync(text, voice, rate * 10, volume);   // -10..10 → проценты
                return null;
            }
            case "tts.pause": _tts.Pause(); return null;
            case "tts.resume": _tts.Resume(); return null;
            case "tts.clear":
                _tts.Clear();
                _edge.Cancel();
                return null;

            /* ---------- оверлей поверх игры ---------- */
            case "overlay.open":
                _win.Invoke(() => EnsureOverlay().Show());
                return null;
            case "overlay.close":
                _win.Invoke(() => _overlay?.Hide());
                return null;
            case "overlay.isVisible":
                return _win.Invoke(() => _overlay?.Visible == true);
            case "overlay.toggle":
                // показать/скрыть окно оверлея (вызывается кнопкой и глобальным хоткеем)
                return _win.Invoke(() =>
                {
                    var ov = EnsureOverlay();
                    if (ov.Visible) { ov.Hide(); return false; }
                    ov.Show();
                    ov.BringToFront();
                    return true;
                });
            case "overlay.set":
            {
                var cfg = ToNode(a.ElementAtOrDefault(0));
                _overlayConfig = cfg?.DeepClone();
                _win.BeginInvoke(() => _overlay?.Apply(cfg));
                return null;
            }
            case "overlay.stats":
            {
                var stats = ToNode(a.ElementAtOrDefault(0));
                _win.BeginInvoke(() => _overlay?.SetStats(stats));
                return null;
            }
            case "overlay.beginDrag":
                _win.BeginInvoke(() => _overlay?.BeginDrag());
                return null;
            case "overlay.center":
                _win.BeginInvoke(() => EnsureOverlay().CenterOnScreen());
                return null;

            /* ---------- каналы: реальные коннекторы площадок ---------- */
            case "channels.connect":
            {
                var sub = ToNode(a.ElementAtOrDefault(0))?.Deserialize<ChannelSubInput>()
                    ?? throw new InvalidOperationException("channels.connect: нет параметров");
                _connectors.Connect(sub);
                return null;
            }
            case "channels.disconnect":
            {
                var sub = ToNode(a.ElementAtOrDefault(0))?.Deserialize<ChannelSubInput>()
                    ?? throw new InvalidOperationException("channels.disconnect: нет параметров");
                _connectors.Disconnect(sub.platform, sub.username);
                return null;
            }
            case "channels.reconnect":
            {
                var sub = ToNode(a.ElementAtOrDefault(0))?.Deserialize<ChannelSubInput>()
                    ?? throw new InvalidOperationException("channels.reconnect: нет параметров");
                _connectors.Connect(sub); // Connect сам гасит предыдущий экземпляр
                return null;
            }

            /* ---------- виджет OBS ---------- */
            case "widget.info":
            {
                if (!_widget.Running) _widget.Start(IntSetting("widgetPort", 8085));
                return new { url = $"http://127.0.0.1:{_widget.Port}/widget", port = _widget.Port, running = _widget.Running };
            }

            /* ---------- глобальные хоткеи ---------- */
            case "hotkeys.apply":
            {
                var map = new Dictionary<string, string>();
                if (a.Length > 0 && ToNode(a[0]) is JsonObject o)
                    foreach (var kv in o) map[kv.Key] = kv.Value?.GetValue<string>() ?? "";
                _win.BeginInvoke(() => _win.ApplyHotkeys(map));
                return null;
            }

            /* ---------- реальная модерация на площадке ---------- */
            case "moderation.run":
            {
                var req = ToNode(a.ElementAtOrDefault(0))
                    ?? throw new InvalidOperationException("moderation.run: нет параметров");
                return await RunModerationAsync(req);
            }

            /* ---------- авторизация чат-бота ---------- */
            case "bot.login":
            {
                // автоматический вход: браузер → площадка → приложение
                var platform = Str(a, 0) ?? "";
                // VK работает только со своим приложением пользователя (Standalone)
                AppAuth.VkClientId = (_settings.TryGetPropertyValue("vkClientId", out var vkId)
                    ? vkId?.GetValue<string>() : null) ?? "";
                var res = await AppAuth.AuthorizeAsync(platform);
                _settings[$"botToken:{platform}"] = res.Token;
                _settings.Remove($"botUserId:{platform}");   // id пересчитается под новый токен
                SaveSettings();
                if (platform == "twitch")
                {
                    // сразу включаем права бота в уже поднятых коннекторах
                    ConnectorManager.BotToken = res.Token;
                    ConnectorManager.BotLogin = res.Account;
                }
                return new { account = res.Account };
            }
            case "bot.verify":
            {
                // запасной путь: пользователь вставил токен вручную
                var platform = Str(a, 0) ?? "";
                var token = (Str(a, 1) ?? "").Trim();
                if (token.Length == 0) throw new InvalidOperationException("Пустой токен");
                var account = await AppAuth.VerifyAsync(platform, token);
                _settings[$"botToken:{platform}"] = token;
                SaveSettings();
                return new { account, token };
            }
            case "bot.logout":
            {
                var platform = Str(a, 0) ?? "";
                _settings.Remove($"botToken:{platform}");
                SaveSettings();
                return null;
            }

            /* ---------- система ---------- */
            case "clipboard.set":
                _win.BeginInvoke(() => { try { Clipboard.SetText(Str(a, 0) ?? ""); } catch { } });
                return null;
            case "shell.openExternal":
                Process.Start(new ProcessStartInfo(Str(a, 0) ?? "") { UseShellExecute = true });
                return null;

            default:
                throw new InvalidOperationException($"unknown method {method}");
        }
    }

    /// Реальные коннекторы (Twitch IRC, Kick pusher, VK/TikTok WS) вызывают это —
    /// сообщение уходит в ленту, оверлей и виджет, как electron-канал "chat".
    /// <summary>
    /// Единая точка входа реального чата. ВАЖНО: вызывается из фоновых потоков
    /// коннекторов, поэтому обращение к WebView2 обязано быть потокобезопасным
    /// и полностью защищённым — иначе исключение убивало коннектор
    /// (Twitch «переподключался» на каждом сообщении) и ломало доставку в оверлей.
    /// </summary>
    public void PushChatMessage(object message)
    {
        try { _win.SendEvent("chat.message", message); } catch { }
        try { _overlay?.Forward("chat.message", message); } catch { }
        try { _widget.Broadcast(message); } catch { }
    }

    /// <summary>
    /// Настоящая модерация Twitch через Helix API (раньше действия были
    /// только локальными: сообщение исчезало в ленте, но на площадке нет).
    /// Требуется токен бота с правами moderator:manage:* и права модератора
    /// на канале. broadcaster_id берём из room-id, полученного в IRC-тегах.
    /// </summary>
    private async Task<object?> RunModerationAsync(JsonNode req)
    {
        var action = req["action"]?.GetValue<string>() ?? "";
        var platform = req["platform"]?.GetValue<string>() ?? "";
        var channel = (req["channel"]?.GetValue<string>() ?? "").ToLowerInvariant();
        var msgId = req["msgId"]?.GetValue<string>();
        var userId = req["userId"]?.GetValue<string>();
        var roomId = req["roomId"]?.GetValue<string>();
        var seconds = req["seconds"] is JsonValue sv && sv.TryGetValue<double>(out var sd) ? (int)sd : 0;

        if (platform != "twitch")
            throw new InvalidOperationException("Модерация пока поддержана только для Twitch");

        var token = _settings["botToken:twitch"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Сначала войдите ботом Twitch в разделе «Чат-бот»");

        if (string.IsNullOrWhiteSpace(roomId))
        {
            lock (TwitchIrcConnector.RoomIds)
                if (TwitchIrcConnector.RoomIds.TryGetValue(channel, out var rid)) roomId = rid;
        }
        if (string.IsNullOrWhiteSpace(roomId))
            throw new InvalidOperationException("Канал ещё не подключён — id канала неизвестен");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(Net.UA);
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        http.DefaultRequestHeaders.Add("Client-Id", AppAuth.TwitchClientId);

        // moderator_id — это сам бот; берём его id у Twitch и кэшируем
        var modId = _settings["botUserId:twitch"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(modId))
        {
            using var vhttp = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            vhttp.DefaultRequestHeaders.Add("Authorization", $"OAuth {token}");
            var vr = await vhttp.GetStringAsync("https://id.twitch.tv/oauth2/validate");
            using var vd = JsonDocument.Parse(vr);
            modId = Net.FindString(vd.RootElement, "user_id") ?? "";
            if (modId.Length == 0) throw new InvalidOperationException("Twitch не вернул id бота");
            _settings["botUserId:twitch"] = modId;
            SaveSettings();
        }

        var baseQuery = $"broadcaster_id={roomId}&moderator_id={modId}";
        HttpResponseMessage res;

        switch (action)
        {
            case "delete":
                if (string.IsNullOrWhiteSpace(msgId))
                    throw new InvalidOperationException("У сообщения нет id Twitch — удалять нечего");
                res = await http.DeleteAsync(
                    $"https://api.twitch.tv/helix/moderation/chat?{baseQuery}&message_id={msgId}");
                break;

            case "mode-clear":
                res = await http.DeleteAsync($"https://api.twitch.tv/helix/moderation/chat?{baseQuery}");
                break;

            case "ban":
            case "timeout":
            {
                if (string.IsNullOrWhiteSpace(userId))
                    throw new InvalidOperationException("Неизвестен id пользователя Twitch");
                var payload = action == "timeout"
                    ? $"{{\"data\":{{\"user_id\":\"{userId}\",\"duration\":{Math.Max(1, seconds)}}}}}"
                    : $"{{\"data\":{{\"user_id\":\"{userId}\"}}}}";
                res = await http.PostAsync($"https://api.twitch.tv/helix/moderation/bans?{baseQuery}",
                    new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
                break;
            }

            case "unban":
                if (string.IsNullOrWhiteSpace(userId))
                    throw new InvalidOperationException("Неизвестен id пользователя Twitch");
                res = await http.DeleteAsync(
                    $"https://api.twitch.tv/helix/moderation/bans?{baseQuery}&user_id={userId}");
                break;

            case "mode-slow":
            case "mode-emote":
            case "mode-followers":
            case "mode-subs":
            {
                var body = action switch
                {
                    "mode-slow" => "{\"slow_mode\":true,\"slow_mode_wait_time\":10}",
                    "mode-emote" => "{\"emote_mode\":true}",
                    "mode-followers" => "{\"follower_mode\":true}",
                    _ => "{\"subscriber_mode\":true}",
                };
                var patch = new HttpRequestMessage(HttpMethod.Patch,
                    $"https://api.twitch.tv/helix/chat/settings?{baseQuery}")
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
                };
                res = await http.SendAsync(patch);
                break;
            }

            default:
                throw new InvalidOperationException($"Неизвестное действие модерации {action}");
        }

        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Twitch отклонил действие ({(int)res.StatusCode}): {body}");
        }
        return new { ok = true };
    }

    /* ---------- settings.json ---------- */
    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
                _settings = JsonNode.Parse(File.ReadAllText(_settingsPath)) as JsonObject ?? new JsonObject();
        }
        catch { _settings = new JsonObject(); }
    }

    private void SaveSettings()
    {
        try { File.WriteAllText(_settingsPath, _settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); }
        catch { /* readonly fs и т.п. — не критично */ }
    }

    private int IntSetting(string key, int fallback) =>
        _settings.TryGetPropertyValue(key, out var n) && n is JsonValue v && v.TryGetValue<int>(out var i) ? i : fallback;

    /* ---------- утилиты аргументов ---------- */
    private static JsonNode? ToNode(object? v)
    {
        if (v is JsonElement el) return el.ValueKind == JsonValueKind.Undefined || el.ValueKind == JsonValueKind.Null ? null : JsonNode.Parse(el.GetRawText());
        if (v is JsonNode n) return n;
        return v == null ? null : JsonValue.Create(v);
    }
    private static string? Str(object?[] a, int i) =>
        a.ElementAtOrDefault(i) is JsonElement el && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    private static bool Bool(object?[] a, int i) =>
        a.ElementAtOrDefault(i) is JsonElement el && el.ValueKind == JsonValueKind.True;
    private static int Int(object?[] a, int i) =>
        a.ElementAtOrDefault(i) is JsonElement el && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n) ? n : 0;

    public void Dispose()
    {
        _connectors.Dispose();
        _tts.Dispose();
        _edge.Dispose();
        _widget.Dispose();
        _overlay?.ForceClose();
    }
}

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
                // EnsureOverlay(), а не _overlay?: настройки (геометрия, сквозной
                // клик, прозрачность) обязаны применяться всегда, иначе интерфейс
                // крутит ползунки, а окно «не меняется».
                _win.BeginInvoke(() => EnsureOverlay().Apply(cfg));
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

            /* ---------- авторизация чат-бота ---------- */
            case "bot.login":
            {
                // автоматический вход: браузер → площадка → страница-мост → приложение
                var platform = Str(a, 0) ?? "";
                var res = await AppAuth.AuthorizeAsync(platform);
                _settings[$"botToken:{platform}"] = res.Token;
                SaveSettings();
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

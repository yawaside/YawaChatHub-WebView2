using System.Drawing;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace YawaChatHub;

/// <summary>
/// Игровой оверлей: второе окно WebView2 поверх всех окон, прозрачное и
/// «клик-сквозь» (аналог electron-окна с setIgnoreMouseEvents).
/// Живая страница — тот же рендерер по адресу #/overlay.
///
/// ПРОЗРАЧНОСТЬ: проверенный рецепт для WebView2 в WinForms — цветовой ключ
/// УНИКАЛЬНОГО цвета (не чёрный, не белый): они совпадают с реальными пикселями
/// рендера (premultiplied-transparent композится в чёрный) и ломают и вид,
/// и мышь. Прозрачные области страницы принимают цвет фона формы (= ключ),
/// DWM вырезает их — и визуально, и для мыши. Видимый контент (подложка rgba,
/// текст, ручка перетаскивания) остаётся кликабельным.
/// DefaultBackgroundColor = Transparent задаётся ДО инициализации WebView2,
/// иначе часть рантаймов его игнорирует.
/// </summary>
public sealed class OverlayWindow : Form
{
    /// Уникальный цветовой ключ: почти чёрный, но не (0,0,0) — чтобы не
    /// пересекаться с пикселями тёмного рендера. Именно такие «некруглые»
    /// значения стабильно работают с WebView2 (rgb(1,2,3) встретить в чате
    /// практически невозможно).
    private static readonly Color KeyColor = Color.FromArgb(255, 1, 2, 3);

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly HostApi _api;
    // сквозной клик ПО УМОЛЧАНИЮ ВЫКЛЮЧЕН: без WS_EX_TRANSPARENT окно
    // нормально принимает мышь; включается хоткеем/настройкой clickThrough
    private bool _clickThrough;
    private bool _freePosition;
    private bool _locked;
    private JsonObject? _pendingConfig;   // конфиг, пришедший до готовности WebView2
    private JsonNode? _lastStats;
    private readonly Queue<string> _pendingEvents = new();
    private readonly object _eventGate = new();
    private bool _ready;

    /* --- нативное перетаскивание по курсору --- */
    private readonly System.Windows.Forms.Timer _dragTimer = new() { Interval = 15 };
    private bool _dragging;
    private Point _grabOffset;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000,
                      WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8, WS_EX_NOACTIVATE = 0x8000000;
    private const uint LWA_COLORKEY = 0x1;
    private const int VK_LBUTTON = 0x01;

    public OverlayWindow(HostApi api)
    {
        _api = api;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        var scr = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Bounds = new Rectangle(scr.Right - 420, scr.Top + 24, 400, scr.Height / 2);
        // цветовой ключ: ВСЕ пиксели этой краски (фон формы = вся область без
        // контента) становятся прозрачными, в т.ч. для мыши
        BackColor = KeyColor;
        TransparencyKey = KeyColor;
        // прозрачный фон WebView2 — ДО создания CoreWebView2 (иначе игнорируется)
        _web.DefaultBackgroundColor = Color.Transparent;
        Controls.Add(_web);

        // ПЕРЕТАСКИВАНИЕ: WebView2 создаёт дочернее окно, которое забирает себе
        // все сообщения мыши — поэтому WM_NCHITTEST/HTCAPTION у формы не срабатывает.
        // Надёжное решение: опрашиваем позицию курсора и состояние ЛКМ напрямую.
        _dragTimer.Tick += (_, _) => DragTick();

        Load += async (_, _) =>
        {
            // общее окружение из AppData (иначе окно создаёт профиль рядом с exe)
            await _web.EnsureCoreWebView2Async(await Program.WebViewEnvAsync());
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            // дублируем после инициализации: прозрачный фон обязателен,
            // иначе страница лежит на непрозрачном чёрном/белом прямоугольнике
            _web.DefaultBackgroundColor = Color.Transparent;

            // ВАЖНО: окно оверлея тоже должно общаться с хостом (settings.get и пр.),
            // иначе страница не получает свои настройки и работает на умолчаниях.
            _web.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                    var root = doc.RootElement;
                    var method = root.GetProperty("method").GetString() ?? "";
                    var id = root.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
                    var args = root.TryGetProperty("args", out var a)
                        ? a.EnumerateArray().Select(x => (object?)x.Clone()).ToArray()
                        : Array.Empty<object?>();
                    _ = _api.DispatchAsync(id, method, args, Reply);
                }
                catch { /* мусорные сообщения игнорируем */ }
            };

            _web.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                _ready = true;
                if (_pendingConfig != null) Forward("overlay.config", _pendingConfig);
                if (_lastStats != null) Forward("overlay.stats", _lastStats);

                string[] queued;
                lock (_eventGate)
                {
                    queued = _pendingEvents.ToArray();
                    _pendingEvents.Clear();
                }
                foreach (var json in queued)
                {
                    try { _web.CoreWebView2.PostWebMessageAsJson(json); } catch { }
                }
            };
            _web.CoreWebView2.Navigate(Program.RendererUrl(false, "/overlay"));
            UpdateExStyle();
        };
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }

    /// Ответ на JSON-RPC вызов страницы оверлея.
    public void Reply(int id, bool ok, object? result, string? error)
    {
        if (_web.CoreWebView2 == null) return;
        var payload = JsonSerializer.Serialize(new { id, ok, result, error });
        BeginInvoke(() =>
        {
            try { _web.CoreWebView2?.PostWebMessageAsJson(payload); } catch { }
        });
    }

    /// Опрос мыши: тянем окно, пока зажата ЛКМ внутри его границ.
    private void DragTick()
    {
        // сквозное или зафиксированное окно не двигается никогда
        if (!_freePosition || _locked || _clickThrough) { _dragging = false; return; }
        if (!GetCursorPos(out var p)) return;
        var cursor = new Point(p.X, p.Y);
        bool pressed = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

        if (!_dragging)
        {
            if (pressed && Bounds.Contains(cursor))
            {
                _dragging = true;
                _grabOffset = new Point(cursor.X - Left, cursor.Y - Top);
            }
            return;
        }

        if (pressed)
        {
            Location = new Point(cursor.X - _grabOffset.X, cursor.Y - _grabOffset.Y);
        }
        else
        {
            _dragging = false;
            PositionChanged?.Invoke(Left, Top);   // сохраняем позицию
        }
    }

    /// <summary>
    /// Применить конфиг из рендерера.
    /// ВАЖНО: прозрачность окна всегда 1 — за прозрачность отвечает ПОДЛОЖКА
    /// внутри страницы (rgba), поэтому текст остаётся полностью чётким
    /// даже при подложке 0%.
    /// </summary>
    public void Apply(JsonNode? cfg)
    {
        if (cfg is not JsonObject o) return;
        _pendingConfig = (JsonObject)o.DeepClone();

        if (o.TryGetPropertyValue("clickThrough", out var ct) && ct is JsonValue cv && cv.TryGetValue<bool>(out var b))
            _clickThrough = b;

        // геометрия: угол экрана + отступы + ширина, либо свободная позиция
        var corner = o.TryGetPropertyValue("corner", out var cr) ? cr?.GetValue<string>() ?? "top-left" : "top-left";
        int width = ReadInt(o, "width", 400);
        int offX = ReadInt(o, "offsetX", 16);
        int offY = ReadInt(o, "offsetY", 16);
        int fontSize = ReadInt(o, "fontSize", 14);
        int maxMsg = ReadInt(o, "maxMessages", 6);
        bool showStats = !o.TryGetPropertyValue("showStats", out var ss) || (ss as JsonValue)?.GetValue<bool>() != false;
        _freePosition = o.TryGetPropertyValue("freePosition", out var fp) && (fp as JsonValue)?.GetValue<bool>() == true;
        _locked = o.TryGetPropertyValue("locked", out var lk) && (lk as JsonValue)?.GetValue<bool>() == true;

        // clickThrough НЕ переопределяем: сквозной клик по умолчанию выключен,
        // включённое состояние выключается тем же хоткеем, а не сменой позиции

        var scr = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        // высота с запасом под строки и панель площадок
        int height = Math.Min(scr.Height - 40, (int)((maxMsg + (showStats ? 1.4 : 0)) * (fontSize * 2.35) + 28));
        height = Math.Max(90, height);
        width = Math.Clamp(width, 240, scr.Width);

        if (_freePosition)
        {
            // сохранённая свободная позиция (или текущая, если окно уже перетащили)
            int px = ReadInt(o, "posX", Left > 0 ? Left : scr.Left + 24);
            int py = ReadInt(o, "posY", Top > 0 ? Top : scr.Top + 24);
            px = Math.Clamp(px, scr.Left - width + 80, scr.Right - 80);
            py = Math.Clamp(py, scr.Top - 10, scr.Bottom - 60);
            Bounds = new Rectangle(px, py, width, height);
        }
        else
        {
            int x = corner.EndsWith("right", StringComparison.Ordinal) ? scr.Right - width - offX : scr.Left + offX;
            int y = corner.StartsWith("bottom", StringComparison.Ordinal) ? scr.Bottom - height - offY : scr.Top + offY;
            Bounds = new Rectangle(x, y, width, height);
        }

        Opacity = 1; // не трогаем текст: прозрачность делает подложка в CSS
        // двигать окно можно, только когда оно НЕ сквозное и НЕ зафиксировано
        SetMovable(_freePosition && !_locked && !_clickThrough);
        UpdateExStyle();

        if (_ready) Forward("overlay.config", _pendingConfig);
    }

    /// <summary>
    /// Режим перемещения — только перетаскивание. Прозрачность окна больше
    /// НЕ зависит от него: DWM-стекло активно всегда, а мышью окно управляет
    /// стилем WS_EX_TRANSPARENT (см. UpdateExStyle), который включает именно
    /// настройка clickThrough / хоткей, а не факт перетаскивания.
    /// </summary>
    private void SetMovable(bool movable)
    {
        if (movable) _dragTimer.Start();
        else { _dragTimer.Stop(); _dragging = false; }
    }

    /// Совместимость: страница может попросить начать перетаскивание,
    /// но фактическим перемещением занимается DragTick() по состоянию мыши.
    public void BeginDrag()
    {
        if (_locked || !_freePosition || _clickThrough) return;
        _dragTimer.Start();
    }

    /// Окно оверлея никогда не забирает фокус у игры.
    protected override bool ShowWithoutActivation => true;

    /// Вернуть окно в центр основного экрана.
    public void CenterOnScreen()
    {
        var scr = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(scr.Left + (scr.Width - Width) / 2, scr.Top + (scr.Height - Height) / 2);
        PositionChanged?.Invoke(Left, Top);
    }

    /// Хост подписывается, чтобы сохранить posX/posY в settings.json.
    public event Action<int, int>? PositionChanged;

    /// Статистика площадок для шапки оверлея (донаты первыми).
    public void SetStats(JsonNode? stats)
    {
        _lastStats = stats?.DeepClone();
        if (_ready && _lastStats != null) Forward("overlay.stats", _lastStats);
    }

    private static int ReadInt(JsonObject o, string key, int fallback) =>
        o.TryGetPropertyValue(key, out var n) && n is JsonValue v && v.TryGetValue<double>(out var d)
            ? (int)Math.Round(d)
            : fallback;

    /// Событие странице оверлея. Вызывается из потоков коннекторов,
    /// поэтому всегда переключаемся в UI-поток окна.
    public void Forward(string type, object payload)
    {
        var json = JsonSerializer.Serialize(new { @event = type, payload });
        if (!_ready)
        {
            lock (_eventGate)
            {
                // Не теряем сообщения, пришедшие в момент загрузки страницы.
                _pendingEvents.Enqueue(json);
                while (_pendingEvents.Count > 100) _pendingEvents.Dequeue();
            }
            return;
        }
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                try { _web.CoreWebView2?.PostWebMessageAsJson(json); } catch { }
            });
        }
        catch { /* окно закрывается */ }
    }

    public void ForceClose() { FormClosing -= null; Dispose(); }

    private void UpdateExStyle()
    {
        int ex = GetWindowLong(Handle, GWL_EXSTYLE);
        // WS_EX_LAYERED обязателен — на нём держится цветовой ключ.
        // NOACTIVATE — окно не перехватывает фокус у полноэкранной игры.
        // WS_EX_TRANSPARENT — только в режиме сквозного клика (хоткей/настройка).
        ex |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
        ex = _clickThrough ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetWindowLong(Handle, GWL_EXSTYLE, ex);
        // Явно переустанавливаем ключ ПОСЛЕ каждой смены стилей: иначе смена
        // layered-стиля через SetWindowLong сбрасывает атрибуты окна, и
        // TransparencyKey перестаёт действовать (окно снова непрозрачное).
        SetLayeredWindowAttributes(Handle, (uint)ColorTranslator.ToWin32(KeyColor), 0, LWA_COLORKEY);
    }
}

/// <summary>
/// Сервер виджета для OBS Browser Source (замена electron-сервера виджета):
/// отдаёт страницу #/widget и стримит сообщения по SSE /widget/events.
/// </summary>
public sealed class WidgetServer : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<HttpListenerResponse> _sseClients = new();
    public int Port = 8085;
    public bool Running => _listener?.IsListening == true;

    public void Start(int port)
    {
        try
        {
            Port = port;
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _ = Task.Run(() => Loop(_cts.Token));
        }
        catch { /* порт занят — виджет недоступен, приложение живёт дальше */ }
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener!.GetContextAsync(); }
            catch { return; }
            _ = Task.Run(() => Serve(ctx));
        }
    }

    private async Task Serve(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";
        try
        {
            if (path.StartsWith("/widget/events"))
            {
                ctx.Response.ContentType = "text/event-stream";
                ctx.Response.Headers.Add("Cache-Control", "no-cache");
                lock (_sseClients) _sseClients.Add(ctx.Response);
                // новый источник OBS сразу получает текущее оформление
                if (_lastConfig != null) SendTo(ctx.Response, "widget.config", _lastConfig);
                return; // соединение держим открытым
            }

            // dist собран single-file — обслуживаемый файл всегда один: index.html
            RendererFiles.EnsureExtracted();
            var local = Path.GetFullPath(Path.Combine(RendererFiles.Dir, "index.html"));
            if (!File.Exists(local))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }
            var bytes = await File.ReadAllBytesAsync(local);
            if (path.StartsWith("/widget"))
            {
                // принудительно открываем виджет-роут вне зависимости от hash
                var html = Encoding.UTF8.GetString(bytes).Replace("</head>", "<script>location.hash='#/widget'+location.search</script></head>");
                bytes = Encoding.UTF8.GetBytes(html);
            }
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
        catch { try { ctx.Response.Abort(); } catch { } }
    }

    private object? _lastConfig;

    /// Оформление виджета → всем источникам, без смены ссылки в OBS.
    public void BroadcastConfig(object? cfg)
    {
        if (cfg == null) return;
        _lastConfig = cfg;
        Push("widget.config", cfg);
    }

    private static void SendTo(HttpListenerResponse response, string type, object payload)
    {
        try
        {
            var data = $"data: {JsonSerializer.Serialize(new { @event = type, payload })}\n\n";
            var bytes = Encoding.UTF8.GetBytes(data);
            response.OutputStream.Write(bytes);
            response.OutputStream.Flush();
        }
        catch { }
    }

    /// Новое сообщение чата → всем открытым OBS-источникам.
    public void Broadcast(object message) => Push("chat.message", message);

    private void Push(string type, object payload)
    {
        var data = $"data: {JsonSerializer.Serialize(new { @event = type, payload })}\n\n";
        var bytes = Encoding.UTF8.GetBytes(data);
        List<HttpListenerResponse> dead = new();
        lock (_sseClients)
        {
            foreach (var r in _sseClients)
            {
                try { r.OutputStream.Write(bytes); r.OutputStream.Flush(); }
                catch { dead.Add(r); }
            }
            foreach (var d in dead) _sseClients.Remove(d);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
    }
}

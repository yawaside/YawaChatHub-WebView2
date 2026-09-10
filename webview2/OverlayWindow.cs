using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace YawaChatHub;

/// <summary>
/// Игровой оверлей: второе окно поверх всех окон, прозрачное и «клик-сквозь»
/// (аналог electron-окна с setIgnoreMouseEvents). Живая страница — тот же
/// рендерер по адресу #/overlay.
///
/// ПРОЗРАЧНОСТЬ — КОНЕЧНЫЙ ВАРИАНТ. Цветовой ключ (TransparencyKey) и
/// DWM-стекло с DirectComposition-содержимым WebView2 работают не везде
/// (проверено в бою: у части систем окно остаётся непрозрачным прямоугольником).
/// Поэтому рендер не зависит от DWM-магии вообще:
///   1. WebView2 живёт в скрытом служебном окне (Opacity = 0 — окно «видимо»
///      для композитора и рендерит страницу, но невидимо для пользователя);
///   2. по изменениям DOM (MutationObserver → overlay.dirty) и раз в секунду
///      страница снимается через CoreWebView2.CapturePreviewAsync;
///   3. PNG перекодируется в 32bpp premultiplied BGRA и подаётся в окно
///      оверлея через UpdateLayeredWindow (ULW_ALPHA) — попиксельная альфа
///      точно как у страницы: подложка rgba полупрозрачна, текст чёткий,
///      пустота абсолютно прозрачна.
/// Бонус: покадровый альфа-буфер даёт хит-тест по пикселям — клики проходят
/// сквозь НЕВИДИМЫЕ области всегда (как в Electron transparent), а режим
/// «сквозной клик» (WS_EX_TRANSPARENT) включается хоткеем/настройкой и
/// пропускает мышь уже над всем окном.
/// </summary>
public sealed class OverlayWindow : Form
{
    /* --- скрытый рендер-хост: здесь живёт настоящий WebView2 --- */
    private readonly RenderHost _renderHost = new();
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly HostApi _api;

    // сквозной клик ПО УМОЛЧАНИЮ ВЫКЛЮЧЕН: включается хоткеем/настройкой
    // clickThrough (тогда мышь проходит и над видимым контентом)
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

    /* --- периодическая перерисовка (страховка + авто-затухание) --- */
    private readonly System.Windows.Forms.Timer _repaintTimer = new() { Interval = 1000 };
    private bool _capturing;
    private bool _captureQueued;

    /* --- GDI для UpdateLayeredWindow --- */
    private IntPtr _hdcScreen;
    private IntPtr _hdcMem;
    private IntPtr _dib;
    private IntPtr _dibPtr;
    private byte[]? _pixels;   // BGRA premultiplied, снапшот кадра (нужен хит-тесту)
    private int _bufW, _bufH;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(
        IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx; public int cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;             // AC_SRC_OVER = 0
        public byte BlendFlags;          // 0
        public byte SourceConstantAlpha; // 255
        public byte AlphaFormat;         // AC_SRC_ALPHA = 1
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;             // отрицательный — top-down
        public short biPlanes;
        public short biBitCount;
        public int biCompression;        // BI_RGB = 0
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000,
                      WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8, WS_EX_NOACTIVATE = 0x8000000;
    private const int VK_LBUTTON = 0x01;
    private const uint ULW_ALPHA = 0x2;
    private const int WM_NCHITTEST = 0x84, WM_ERASEBKGND = 0x14;
    private const uint SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_FRAMECHANGED = 0x20;
    private const int HTCLIENT = 1, HTTRANSPARENT = -1;
    /// Клик проходит насквозь там, где альфа страницы ниже этого порога.
    private const byte AlphaHitThreshold = 12;

    [DllImport("user32.dll")] private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    /// <summary>
    /// Окно рождается сразу layered: выставлять WS_EX_LAYERED через
    /// CreateParams надёжнее, чем потом SetWindowLong (переключение стиля
    /// на живом окне у части систем сбрасывает отрисовку ULW-кадра).
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    /* КЛЮЧЕВОЕ для ULW-окна: собственная отрисовка формы запрещена.
       Иначе стандартный цикл GDI (фон BackColor) стирает кадр, выведенный
       UpdateLayeredWindow, и получаем сплошной непрозрачный прямоугольник —
       ровно то, что наблюдалось. Контент окна — ТОЛЬКО из UpdateLayeredWindow. */
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { PresentFrame(); }

    /* Перемещение/размер: ULW-кадр пере-выкладываем, чтобы DWM не рисовал
       старое содержимое в новых границах. */
    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        PresentFrame();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // под новый размер — прозрачный кадр, страницу доснимет observer/таймер
        if (Visible && WindowState == FormWindowState.Normal && Width > 2 && Height > 2)
            PresentBlank(Width, Height);
    }

    public OverlayWindow(HostApi api)
    {
        _api = api;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        var scr = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Bounds = new Rectangle(scr.Right - 420, scr.Top + 24, 400, scr.Height / 2);
        BackColor = Color.Black; // на экран не попадает: окно рисуется только UpdateLayeredWindow

        // скрытый рендер-хост: WebView2 внутри него рендерит страницу,
        // окно «видимо» для DWM (Opacity = 0 — композиция идёт, пользователю не видно)
        _renderHost.Controls.Add(_web);
        _renderHost.Size = new Size(Bounds.Width, Bounds.Height);

        // ПЕРЕТАСКИВАНИЕ: опрашиваем позицию курсора и состояние ЛКМ напрямую.
        _dragTimer.Tick += (_, _) => DragTick();
        _repaintTimer.Tick += (_, _) => ScheduleCapture();

        Load += async (_, _) =>
        {
            _renderHost.Show();
            // общее окружение из AppData (иначе окно создаёт профиль рядом с exe)
            await _web.EnsureCoreWebView2Async(await Program.WebViewEnvAsync());
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            // прозрачный фон — снимок сразу получает корректную альфу
            _web.DefaultBackgroundColor = Color.Transparent;

            // Любое изменение DOM → переснять кадр. Покрывает всё: новые
            // сообщения, статистика, автозатухание, смена настроек — без
            // правок самой страницы.
            await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("""
                (() => {
                  let t = null;
                  const send = () => { t = null; try { window.chrome.webview.postMessage({ method: 'overlay.dirty' }); } catch (e) {} };
                  const hook = () => {
                    try {
                      new MutationObserver(() => { if (t == null) t = setTimeout(send, 60); })
                        .observe(document.documentElement, { subtree: true, childList: true, attributes: true, characterData: true });
                    } catch (e) {}
                  };
                  window.addEventListener('load', hook);
                  setTimeout(send, 300);
                  setTimeout(send, 900);
                })();
                """);

            _web.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                    var root = doc.RootElement;
                    var method = root.GetProperty("method").GetString() ?? "";
                    if (method == "overlay.dirty")
                    {
                        ScheduleCapture();
                        return;
                    }
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
                ScheduleCapture();
            };
            _web.CoreWebView2.Navigate(Program.RendererUrl(false, "/overlay"));
            _repaintTimer.Start();
            UpdateExStyle();
        };
        // при появлении окна сразу переснимаем кадр (без секунды пустоты)
        VisibleChanged += (_, _) => ScheduleCapture();
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }

    /// <summary>
    /// Скрытое окно-хост рендера: Opacity = 0 → WebView2 активно рисует
    /// страницу (композитор жив, CapturePreview работает), но пользователю
    /// окно невидимо и мышь не перехватывает.
    /// </summary>
    private sealed class RenderHost : Form
    {
        public RenderHost()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Opacity = 0;
            var scr = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            Bounds = new Rectangle(scr.Right + 64, scr.Top + 64, 400, 300);
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // невидимое служебное окно не должно ловить мышь и фокус
            const int toolNoActivateTransparent = 0x80 | 0x8000000 | 0x20;
            int ex = GetWindowLong(Handle, GWL_EXSTYLE);
            SetWindowLong(Handle, GWL_EXSTYLE, ex | toolNoActivateTransparent);
        }
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

    /* ================= съёмка кадра и вывод через UpdateLayeredWindow ================= */

    /// Запланировать пересъёмку страницы (коалесится, всегда в UI-потоке).
    private void ScheduleCapture()
    {
        if (IsDisposed || !IsHandleCreated || !Visible) return;
        if (_captureQueued) return;
        _captureQueued = true;
        BeginInvoke(async () =>
        {
            _captureQueued = false;
            await CaptureOnceAsync();
        });
    }

    private async Task CaptureOnceAsync()
    {
        if (_capturing || IsDisposed || !Visible) return;
        _capturing = true;
        try
        {
            if (_web.CoreWebView2 == null) return;
            var w = _renderHost.ClientSize.Width;
            var h = _renderHost.ClientSize.Height;
            if (w < 2 || h < 2) return;

            using var ms = new MemoryStream();
            await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
            ms.Position = 0;

            using var png = Image.FromStream(ms);
            int cw = Math.Min(w, png.Width), ch = Math.Min(h, png.Height);
            if (cw < 2 || ch < 2) return;

            // 32bpp premultiplied — формат, который ждёт UpdateLayeredWindow
            using var bmp = new Bitmap(cw, ch, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
                g.DrawImage(png, 0, 0, cw, ch);

            var rect = new Rectangle(0, 0, cw, ch);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);

            EnsureGdi();
            EnsureDib(cw, ch);
            _pixels ??= new byte[cw * ch * 4];
            for (int y = 0; y < ch; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, _pixels, y * cw * 4, cw * 4);
            bmp.UnlockBits(data);

            Marshal.Copy(_pixels, 0, _dibPtr, _pixels.Length);
            PresentFrame();
        }
        catch { /* снимок не удался — следующий тик/событие повторит */ }
        finally
        {
            _capturing = false;
        }
    }

    private void EnsureGdi()
    {
        if (_hdcScreen == IntPtr.Zero) _hdcScreen = GetDC(IntPtr.Zero);
        if (_hdcMem == IntPtr.Zero) _hdcMem = CreateCompatibleDC(_hdcScreen);
    }

    /// Создаёт DIB-секцию текущего размера окна (создаётся заново при resize).
    private void EnsureDib(int w, int h)
    {
        if (_dib != IntPtr.Zero && _bufW == w && _bufH == h) return;
        if (_dib != IntPtr.Zero)
        {
            DeleteObject(_dib);
            _dib = IntPtr.Zero;
        }
        var bmi = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h,          // top-down: строка 0 — верхняя
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,      // BI_RGB
        };
        _dib = CreateDIBSection(_hdcMem, ref bmi, 0, out _dibPtr, IntPtr.Zero, 0);
        SelectObject(_hdcMem, _dib);
        _pixels = new byte[w * h * 4]; // также снапшот альфы для хит-теста
        Marshal.Copy(_pixels, 0, _dibPtr, _pixels.Length);
        _bufW = w;
        _bufH = h;
    }

    /// Вывод текущего кадра в окно оверлея с per-pixel альфой.
    private void PresentFrame()
    {
        if (!IsHandleCreated || _dib == IntPtr.Zero || _bufW < 2 || _bufH < 2) return;
        var dst = new POINT { X = Left, Y = Top };
        var size = new SIZE { cx = _bufW, cy = _bufH };
        var src = new POINT { X = 0, Y = 0 };
        var blend = new BLENDFUNCTION
        {
            BlendOp = 0,      // AC_SRC_OVER
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1,  // AC_SRC_ALPHA
        };
        try { UpdateLayeredWindow(Handle, _hdcScreen, ref dst, ref size, _hdcMem, ref src, 0, ref blend, ULW_ALPHA); }
        catch { }
    }

    /// Пустой (полностью прозрачный) кадр — пока страница ещё грузится/ресайзимся.
    private void PresentBlank(int w, int h)
    {
        if (!IsHandleCreated || w < 2 || h < 2) return;
        EnsureGdi();
        EnsureDib(w, h);
        Array.Clear(_pixels!, 0, _pixels!.Length);
        Marshal.Copy(_pixels, 0, _dibPtr, _pixels.Length);
        PresentFrame();
    }

    /// Альфа пикселя окна по экранным координатам (0 — там смотрит сквозь).
    private byte PixelAlphaAtScreen(int screenX, int screenY)
    {
        var p = _pixels;
        if (p == null || _bufW < 1 || _bufH < 1) return 0;
        int x = screenX - Left;
        int y = screenY - Top;
        if (x < 0 || y < 0 || x >= _bufW || y >= _bufH) return 0;
        return p[(y * _bufW + x) * 4 + 3];
    }

    /* ================= мышь: хит-тест по альфе + перетаскивание ================= */

    protected override void WndProc(ref Message m)
    {
        // GDI-стирание фона запрещено: окно рисует только UpdateLayeredWindow
        if (m.Msg == WM_ERASEBKGND)
        {
            m.Result = (IntPtr)1;
            return;
        }
        // Невидимые пиксели пропускают клики ВСЕГДА (как Electron transparent),
        // видимый контент — интерактивен. Полный сквозной клик (все пиксели)
        // включает стиль WS_EX_TRANSPARENT (UpdateExStyle), сюда он даже
        // хит-тест не присылает.
        if (m.Msg == WM_NCHITTEST && !_clickThrough)
        {
            int sx = unchecked((short)(m.LParam.ToInt64() & 0xFFFF));
            int sy = unchecked((short)((m.LParam.ToInt64() >> 16) & 0xFFFF));
            m.Result = PixelAlphaAtScreen(sx, sy) >= AlphaHitThreshold
                ? (IntPtr)HTCLIENT
                : (IntPtr)HTTRANSPARENT;
            return;
        }
        base.WndProc(ref m);
    }

    /// Опрос мыши: тянем окно, пока зажата ЛКМ над видимым пикселем.
    private void DragTick()
    {
        // сквозное или зафиксированное окно не двигается никогда
        if (!_freePosition || _locked || _clickThrough) { _dragging = false; return; }
        if (!GetCursorPos(out var p)) return;
        var cursor = new Point(p.X, p.Y);
        bool pressed = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

        if (!_dragging)
        {
            if (pressed && Bounds.Contains(cursor) && PixelAlphaAtScreen(cursor.X, cursor.Y) >= AlphaHitThreshold)
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
    /// Прозрачность текста/подложки — внутри страницы (rgba), а оконная
    /// альфа получается из снимка автоматически.
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

        // рендер-хост того же размера — снимок 1:1 с окном оверлея
        _renderHost.Size = new Size(Width, Height);

        // двигать окно можно, только когда оно НЕ сквозное и НЕ зафиксировано
        SetMovable(_freePosition && !_locked && !_clickThrough);
        UpdateExStyle();

        // под новый размер — прозрачный кадр, страницу доснимет MutationObserver
        PresentBlank(Width, Height);
        ScheduleCapture();

        if (_ready) Forward("overlay.config", _pendingConfig);
    }

    /// <summary>
    /// Режим перемещения — только перетаскивание. Прозрачность от него не
    /// зависит: мышь над всем окном пропускает стиль WS_EX_TRANSPARENT
    /// (включается настройкой clickThrough / хоткеем), а невидимые пиксели
    /// пропускают её всегда — хит-тестом по альфе.
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _repaintTimer.Dispose(); } catch { }
            try { _dragTimer.Dispose(); } catch { }
            try { _renderHost.Dispose(); } catch { }
        }
        if (_dib != IntPtr.Zero) { DeleteObject(_dib); _dib = IntPtr.Zero; }
        if (_hdcMem != IntPtr.Zero) { DeleteDC(_hdcMem); _hdcMem = IntPtr.Zero; }
        if (_hdcScreen != IntPtr.Zero) { ReleaseDC(IntPtr.Zero, _hdcScreen); _hdcScreen = IntPtr.Zero; }
        base.Dispose(disposing);
    }

    private void UpdateExStyle()
    {
        int ex = GetWindowLong(Handle, GWL_EXSTYLE);
        // WS_EX_LAYERED обязателен — через него работает UpdateLayeredWindow.
        // NOACTIVATE — окно не перехватывает фокус у полноэкранной игры.
        // WS_EX_TRANSPARENT — только в режиме сквозного клика (хоткей/настройка).
        ex |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
        ex = _clickThrough ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetWindowLong(Handle, GWL_EXSTYLE, ex);
        // SetWindowLong сам по себе не применяет стили — нужен FRAMECHANGED,
        // а после смены стилей ULW-кадр пере-выкладываем (иначе DWM может
        // стереть содержимое окна при системной перерисовке).
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        PresentFrame();
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

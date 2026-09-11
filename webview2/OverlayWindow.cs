using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace YawaChatHub;

/// <summary>
/// Игровой оверлей поверх всех окон.
///
/// РЕНДЕР — ПОЛНОСТЬЮ НАТИВНЫЙ (без WebView2 в этом окне). Браузерный путь
/// для оверлея оказался недетерминированным: на разных комбинациях
/// Windows/Runtime/DPM прозрачность то теряется, то приходит чёрной, а
/// скриншот-пайплайны (CapturePreview) альфу не отдают в принципе.
///
/// Здесь кадр рисуется нашим кодом GDI+ на прозрачном 32bpp premultiplied
/// битмапе и вылетает в окно через UpdateLayeredWindow: полупрозрачная
/// подложка, чёткий текст, абсолютно прозрачная пустота — попиксельная
/// альфа автора, не зависящая ни от чего. Бонус: хит-тест по реальным
/// пикселям кадра (невидимые области всегда пропускают клики, как в
/// Electron transparent), сквозной клик над всем окном — хоткей/настройка.
///
/// Публичный интерфейс не изменился: HostApi кормит Apply/SetStats/Forward
/// теми же JSON-сообщениями чата, конфигом и статистикой площадок.
/// </summary>
public sealed class OverlayWindow : Form
{
    private readonly HostApi _api;

    // сквозной клик ПО УМОЛЧАНИЮ ВЫКЛЮЧЕН: включается хоткеем/настройкой
    private bool _clickThrough;
    private bool _freePosition = true;
    private bool _locked;
    private JsonObject? _pendingConfig;   // конфиг, пришедший до создания окна
    private bool _ready;

    /* --- очередь событий до готовности окна --- */
    private readonly object _eventGate = new();
    private readonly Queue<Action> _pending = new();

    /* --- конфиг рендера (значения совпадают с DEFAULT_OVERLAY) --- */
    private sealed class Cfg
    {
        public string StyleId = "glass";
        public string FontFamily = "inter";
        public string Corner = "free", Growth = "down", StatsPos = "bottom";
        public int Width = 400, OffX = 16, OffY = 16, FontSize = 14, MaxMessages = 6,
                   Spacing = 4, Radius = 10, FadeAfterSec = 30, PosX = 24, PosY = 24;
        public double BackdropOpacity = 0.55, TextOpacity = 1;
        public bool ShowStats = true, ShowDonations = true, ShowViewers = true,
                    StatsCompact, HideDisconnected = true, AutoFade, ShowTime,
                    ShowPlatform = true, ShowEvents, AccentBar = true, ShadowText = true;
    }
    private readonly Cfg _cfg = new();

    /* --- данные --- */
    private sealed class Msg
    {
        public string Platform = "", Author = "", Text = "", Kind = "chat",
                      ColorHex = "", Currency = "";
        public double? Amount;
        public long Born, Ts;
        public Color AuthorColor = Color.White;
    }
    private readonly List<Msg> _messages = new();

    private sealed class PlatformStat
    {
        public string Platform = "";
        public bool Online, Connected;
        public int Viewers, Messages;
    }
    private List<PlatformStat> _stats = new();
    private double _donationsTotal;
    private string _donationsCurrency = "₽";
    private int _donationsCount;

    /* --- перетаскивание --- */
    private readonly System.Windows.Forms.Timer _dragTimer = new() { Interval = 15 };
    private bool _dragging;
    private Point _grabOffset;

    /* --- авто-затухание строк --- */
    private readonly System.Windows.Forms.Timer _fadeTimer = new() { Interval = 1000 };

    /* --- GDI для UpdateLayeredWindow --- */
    private IntPtr _hdcScreen;
    private IntPtr _hdcMem;
    private IntPtr _dib;
    private IntPtr _dibPtr;
    private byte[]? _pixels;   // BGRA premultiplied — также буфер хит-теста
    private int _bufW, _bufH;

    /* --- кэш шрифтов --- */
    private readonly Dictionary<string, Font> _fonts = new();

    /* --- диагностика: %LOCALAPPDATA%\YawaChatHub\overlay-debug.log --- */
    private static string LogPath => Path.Combine(Program.AppDir, "overlay-debug.log");
    private static void Dbg(string message)
    {
        try
        {
            var fi = new FileInfo(LogPath);
            if (fi.Exists && fi.Length > 256 * 1024) File.WriteAllText(LogPath, "--- log cleared ---\n");
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
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
    private const int WM_NCHITTEST = 0x84, WM_ERASEBKGND = 0x14, WM_MOUSEACTIVATE = 0x21;
    private const int MA_NOACTIVATE = 3;
    private const uint SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_FRAMECHANGED = 0x20;
    private const int HTCLIENT = 1, HTTRANSPARENT = -1;
    /// Клик проходит насквозь там, где альфа кадра ниже этого порога.
    private const byte AlphaHitThreshold = 12;

    /// <summary>
    /// ULW-окно рождается сразу layered. Контент — только из
    /// UpdateLayeredWindow; GDI-фон формы не рисуется никогда.
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

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { PresentFrame(); }

    public OverlayWindow(HostApi api)
    {
        _api = api;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        var scr = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Bounds = new Rectangle(scr.Right - 420, scr.Top + 24, 400, scr.Height / 2);
        BackColor = Color.Black; // на экран не попадает: окно рисует только ULW

        // перетаскивание по состоянию курсора (над видимым пикселем)
        _dragTimer.Tick += (_, _) => DragTick();
        _fadeTimer.Tick += (_, _) =>
        {
            if (!_cfg.AutoFade) return;
            long cutoff = Environment.TickCount64 - _cfg.FadeAfterSec * 1000L;
            if (_messages.RemoveAll(m => m.Born < cutoff) > 0) Repaint();
        };
        // авто-затухание строк перерисовывает кадр
        VisibleChanged += (_, _) =>
        {
            if (Visible) { _fadeTimer.Start(); PresentBlank(Width, Height); Repaint(); }
        };
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Action[] queued;
        lock (_eventGate)
        {
            _ready = true;
            queued = _pending.ToArray();
            _pending.Clear();
        }
        foreach (var a in queued) a();
        UpdateExStyle();
    }

    /// Совместимость с рендерером (старое окно было WebView): в нативном
    /// окне ответы странице не нужны; метод оставлен для HostApi.
    public void Reply(int id, bool ok, object? result, string? error) { _ = (id, ok, result, error); }

    /* ================= применение конфига ================= */

    /// Применить конфиг из рендерера: геометрия + стили отрисовки.
    public void Apply(JsonNode? cfg)
    {
        if (cfg is not JsonObject o) return;
        _pendingConfig = (JsonObject)o.DeepClone();

        BeginInvoke(() => ApplyNow(o));
    }

    private void ApplyNow(JsonObject o)
    {
        if (Bool(o, "clickThrough", out var ct)) _clickThrough = ct;
        if (Str(o, "corner", out var c)) _cfg.Corner = c;
        if (Str(o, "growth", out var g)) _cfg.Growth = g;
        if (Str(o, "statsPos", out var sp)) _cfg.StatsPos = sp;
        _cfg.Width = Int(o, "width", _cfg.Width);
        _cfg.OffX = Int(o, "offsetX", _cfg.OffX);
        _cfg.OffY = Int(o, "offsetY", _cfg.OffY);
        _cfg.FontSize = Math.Clamp(Int(o, "fontSize", _cfg.FontSize), 9, 40);
        _cfg.MaxMessages = Math.Clamp(Int(o, "maxMessages", _cfg.MaxMessages), 1, 40);
        _cfg.Spacing = Int(o, "spacing", _cfg.Spacing);
        _cfg.Radius = Int(o, "radius", _cfg.Radius);
        _cfg.FadeAfterSec = Int(o, "fadeAfterSec", _cfg.FadeAfterSec);
        _cfg.PosX = Int(o, "posX", _cfg.PosX);
        _cfg.PosY = Int(o, "posY", _cfg.PosY);
        _cfg.BackdropOpacity = Dbl(o, "backdropOpacity", _cfg.BackdropOpacity);
        _cfg.TextOpacity = Dbl(o, "textOpacity", _cfg.TextOpacity);
        if (Bool(o, "showStats", out var ss)) _cfg.ShowStats = ss;
        if (Bool(o, "showDonations", out var sd)) _cfg.ShowDonations = sd;
        if (Bool(o, "showViewers", out var sv)) _cfg.ShowViewers = sv;
        if (Bool(o, "statsCompact", out var sc)) _cfg.StatsCompact = sc;
        if (Bool(o, "hideDisconnected", out var hd)) _cfg.HideDisconnected = hd;
        if (Bool(o, "autoFade", out var af)) _cfg.AutoFade = af;
        if (Bool(o, "showTime", out var st)) _cfg.ShowTime = st;
        if (Bool(o, "showPlatform", out var spl)) _cfg.ShowPlatform = spl;
        if (Bool(o, "showEvents", out var se)) _cfg.ShowEvents = se;
        if (Bool(o, "accentBar", out var ab)) _cfg.AccentBar = ab;
        if (Bool(o, "shadowText", out var sht)) _cfg.ShadowText = sht;
        if (Bool(o, "locked", out var lk)) _locked = lk;
        if (Bool(o, "freePosition", out var fp)) _freePosition = fp;

        if (Str(o, "fontFamily", out var ff)) _cfg.FontFamily = ff;
        if (Str(o, "styleId", out var si)) _cfg.StyleId = si;

        // геометрия (как в веб-версии)
        var scr = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        int height = Math.Min(scr.Height - 40, (int)((_cfg.MaxMessages + (_cfg.ShowStats ? 1.4 : 0)) * (_cfg.FontSize * 2.35) + 28));
        height = Math.Max(90, height);
        int width = Math.Clamp(_cfg.Width, 240, scr.Width);

        if (_freePosition)
        {
            int px = Math.Clamp(_cfg.PosX, scr.Left - width + 80, scr.Right - 80);
            int py = Math.Clamp(_cfg.PosY, scr.Top - 10, scr.Bottom - 60);
            Bounds = new Rectangle(px, py, width, height);
        }
        else
        {
            int x = _cfg.Corner.EndsWith("right", StringComparison.Ordinal) ? scr.Right - width - _cfg.OffX : scr.Left + _cfg.OffX;
            int y = _cfg.Corner.StartsWith("bottom", StringComparison.Ordinal) ? scr.Bottom - height - _cfg.OffY : scr.Top + _cfg.OffY;
            Bounds = new Rectangle(x, y, width, height);
        }

        Opacity = 1;
        SetMovable(_freePosition && !_locked && !_clickThrough);
        UpdateExStyle();
        Repaint();
    }

    private void SetMovable(bool movable)
    {
        if (movable) _dragTimer.Start();
        else { _dragTimer.Stop(); _dragging = false; }
    }

    public void BeginDrag()
    {
        if (_locked || !_freePosition || _clickThrough) return;
        _dragTimer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    public void CenterOnScreen()
    {
        var scr = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(scr.Left + (scr.Width - Width) / 2, scr.Top + (scr.Height - Height) / 2);
        PositionChanged?.Invoke(Left, Top);
        Repaint();
    }

    public event Action<int, int>? PositionChanged;

    /* ================= события от HostApi ================= */

    /// Статистика площадок (донаты первыми).
    public void SetStats(JsonNode? stats) => Forward("overlay.stats", stats ?? JsonValue.Create(0)!);

    /// Событие от хоста: chat.message / overlay.stats / overlay.config.
    public void Forward(string type, object payload)
    {
        BeginInvoke(() => SafeDispatch(type, payload));
    }

    private void SafeDispatch(string type, object payload)
    {
        if (!_ready)
        {
            lock (_eventGate)
            {
                _pending.Enqueue(() => SafeDispatch(type, payload));
            }
            return;
        }
        try { Dispatch(type, payload); }
        catch (Exception ex) { Dbg($"dispatch {type} fail: {ex.GetType().Name}: {ex.Message}"); }
    }

    private void Dispatch(string type, object payload)
    {
        string json = payload switch
        {
            null => "null",
            string s => s,
            _ => JsonSerializer.Serialize(payload),
        };
        if (type == "overlay.config")
        {
            try { ApplyNow(JsonNode.Parse(json) as JsonObject ?? new JsonObject()); } catch { }
            return;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (type == "overlay.stats")
        {
            _stats.Clear();
            if (root.TryGetProperty("donations", out var d))
            {
                _donationsTotal = TryDouble(d, "total", 0);
                _donationsCurrency = TryString(d, "currency", "₽");
                _donationsCount = (int)TryDouble(d, "count", 0);
            }
            if (root.TryGetProperty("platforms", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in arr.EnumerateArray())
                {
                    _stats.Add(new PlatformStat
                    {
                        Platform = TryString(p, "platform", ""),
                        Online = TryBool(p, "online", false),
                        Connected = TryBool(p, "connected", false),
                        Viewers = (int)TryDouble(p, "viewers", 0),
                        Messages = (int)TryDouble(p, "messages", 0),
                    });
                }
            }
            Repaint();
            return;
        }

        if (type != "chat.message") return;

        var m = new Msg
        {
            Platform = TryString(root, "platform", "twitch"),
            Author = TryString(root, "author", ""),
            Text = TryString(root, "text", ""),
            Kind = TryString(root, "kind", "chat"),
            ColorHex = TryString(root, "color", ""),
            Currency = TryString(root, "currency", ""),
            Born = Environment.TickCount64,
            Ts = (long)TryDouble(root, "ts", 0),
        };
        if (root.TryGetProperty("amount", out var am) && am.ValueKind == JsonValueKind.Number)
            m.Amount = am.GetDouble();

        // события — только при включённой настройке
        if (m.Kind != "chat" && m.Kind != "system" && !_cfg.ShowEvents) return;
        if (!string.IsNullOrWhiteSpace(m.Text) || m.Amount.HasValue || m.Kind != "chat")
            PushMessage(m);
    }

    private void PushMessage(Msg m)
    {
        // строкам в оверлее по-прежнему максимум maxMessages, донаты первыми как текст
        _messages.Add(m);
        ResetMessageColors();
        if (_messages.Count > _cfg.MaxMessages * 2)
            _messages.RemoveRange(0, _messages.Count - _cfg.MaxMessages * 2);
        Repaint();
    }

    private void ResetMessageColors()
    {
        foreach (var msg in _messages) msg.AuthorColor = ParseColor(msg.ColorHex);
    }

    /* ================= отрисовка ================= */

    private bool _painting;

    /// Главный цикл: GDI+ на прозрачном PArgb-битмапе → DIB → ULW.
    private void Repaint()
    {
        if (_painting || IsDisposed || !Visible || !IsHandleCreated) return;
        _painting = true;
        try
        {
            int w = Math.Max(2, Width), h = Math.Max(2, Height);
            EnsureGdi();
            EnsureDib(w, h);

            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            DrawScene(g, w, h);

            var rect = new Rectangle(0, 0, w, h);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            _pixels ??= new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, _pixels, y * w * 4, w * 4);
            bmp.UnlockBits(data);

            Marshal.Copy(_pixels, 0, _dibPtr, _pixels.Length);
            PresentFrame();
        }
        catch (Exception ex)
        {
            // одна неудачная перерисовка не ломает оверлей — но фиксируем причину
            Dbg($"repaint fail: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _painting = false;
        }
    }

    private void DrawScene(Graphics g, int w, int h)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        int pad = 10;
        int fs = _cfg.FontSize;
        int statsH = _cfg.ShowStats ? Math.Max(18, (int)(fs * 1.9)) : 0;

        var stats = VisibleStats();

        // сообщения: до maxMessages, свежие ближе к главному краю
        IEnumerable<Msg> seq = _messages;
        if (_cfg.AutoFade)
        {
            long cutoff = Environment.TickCount64 - _cfg.FadeAfterSec * 1000L;
            seq = seq.Where(m => m.Born >= cutoff);
        }
        var list = seq.TakeLast(_cfg.MaxMessages).ToList();

        // измеряем блоки заранее: сообщение занимает 1–2 строки текста
        var blocks = list.Select(m => (m, height: MeasureMessage(g, m, w - pad * 2))).ToList();

        // ручка перетаскивания занимает своё место
        bool movable = _freePosition && !_locked && !_clickThrough;
        int handleH = movable ? Math.Max(18, fs + 8) + _cfg.Spacing : 0;
        int topStatsH = stats != null && _cfg.StatsPos == "top" ? statsH + _cfg.Spacing : 0;
        int bottomStatsH = stats != null && _cfg.StatsPos == "bottom" ? statsH + _cfg.Spacing : 0;

        if (blocks.Count == 0 && stats == null)
        {
            DrawHint(g, pad, pad, fs);
            return;
        }

        int availH = h - pad * 2 - handleH - topStatsH - bottomStatsH;
        int totalH = blocks.Sum(b => b.height + _cfg.Spacing);

        // growth="up": лента прижата ВНИЗ (как в чатах), "down" — вверх
        int y = _cfg.Growth == "up"
            ? pad + handleH + topStatsH + Math.Max(0, availH - totalH)
            : pad + handleH + topStatsH;

        // порядок всегда старые → новые; growth задаёт лишь якорь ленты
        var ordered = blocks;

        if (movable) DrawDragHandle(g, pad, pad, fs);
        if (stats != null && _cfg.StatsPos == "top")
            DrawStatsBar(g, pad, pad + handleH, w - pad * 2, statsH, stats);

        foreach (var (m, bh) in ordered)
        {
            if (y + bh > h - pad - bottomStatsH) break;
            // одна «битая» строка не должна гасить весь кадр
            try { DrawMessage(g, m, pad, y, w - pad * 2, bh); }
            catch (Exception ex) { Dbg($"msg draw fail: {ex.GetType().Name}: {ex.Message}"); }
            y += bh + _cfg.Spacing;
        }

        if (stats != null && _cfg.StatsPos == "bottom")
            DrawStatsBar(g, pad, h - pad - statsH, w - pad * 2, statsH, stats);
    }

    private List<PlatformStat>? VisibleStats()
    {
        if (!_cfg.ShowStats) return null;
        var shown = _cfg.HideDisconnected
            ? _stats.Where(p => p.Online || p.Connected || p.Messages > 0).ToList()
            : _stats.ToList();
        if (shown.Count == 0 && !_cfg.ShowDonations) return null;
        return shown;
    }

    /// Высота блока сообщения: шапка + 1–2 строки текста.
    private int MeasureMessage(Graphics g, Msg m, int width)
    {
        int fs = _cfg.FontSize;
        int padV = Math.Max(4, (int)(fs * 0.55));
        int headerH = (int)(fs * 1.55);
        if (string.IsNullOrWhiteSpace(m.Text))
            return padV * 2 + headerH;

        var bodyFont = GetFont(fs, bold: false);
        var lines = WrapText(g, m.Text, bodyFont, Math.Max(40, width - 18), 2);
        return padV * 2 + headerH + lines.Count * (int)(fs * 1.38);
    }

    private void DrawMessage(Graphics g, Msg m, int x, int y, int width, int height)
    {
        int fs = _cfg.FontSize;
        byte textA = Alpha(_cfg.TextOpacity);
        int padV = Math.Max(4, (int)(fs * 0.55));

        // подложка + тонкая рамка (стеклянный вид, как в веб-стилях)
        var bounds = new Rectangle(x, y, width, height - 1);
        if (_cfg.BackdropOpacity > 0.02)
        {
            FillRounded(g, bounds,
                Color.FromArgb(Math.Min(255, (int)(_cfg.BackdropOpacity * 255)), 9, 11, 19), _cfg.Radius);
            StrokeRounded(g, bounds,
                Color.FromArgb(Math.Min(110, (int)(_cfg.BackdropOpacity * 200)), 255, 255, 255), _cfg.Radius);
        }
        if (_cfg.AccentBar)
            FillRounded(g, new Rectangle(x + 3, y + padV, 3, height - padV * 2 - 1),
                ScaleAlpha(PlatformColor(m.Platform), textA), 2);

        int tx = x + 9;
        int ty = y + padV - 1;
        const int gap = 5;

        // время
        if (_cfg.ShowTime && m.Ts > 0)
        {
            var t = DateTimeOffset.FromUnixTimeMilliseconds(m.Ts).ToLocalTime();
            tx += DrawSeg(g, t.ToString("HH:mm"), GetFont(Math.Max(9, (int)(fs * 0.82)), bold: false),
                ScaleAlpha(Color.FromArgb(255, 139, 145, 168), textA), tx, ty, fs) + gap;
        }
        // значок площадки — настоящий мини-глиф, как в веб-фиде
        if (_cfg.ShowPlatform)
        {
            var chip = new Rectangle(tx, ty + (int)(fs * 0.14), (int)(fs * 1.45), (int)(fs * 1.45));
            tx += DrawPlatformGlyph(g, m.Platform, chip, textA) + gap;
        }
        // ник
        if (!string.IsNullOrEmpty(m.Author))
            tx += DrawSeg(g, m.Author + ": ", GetFont(fs, bold: true), ScaleAlpha(m.AuthorColor, textA), tx, ty, fs) + gap;
        // донат-плашка
        if (m.Amount.HasValue)
        {
            var amt = $"{m.Amount.Value:0.##} {m.Currency}";
            var pf = GetFont(fs, bold: true);
            int aw = TextRenderer.MeasureText(amt + "  $", pf).Width;
            var pill = new Rectangle(tx, ty + (int)(fs * 0.1), aw + 10, (int)(fs * 1.4));
            FillRounded(g, pill, Color.FromArgb(Math.Min(255, (int)textA), 66, 45, 6), 5);
            StrokeRounded(g, pill, Color.FromArgb(Math.Min(255, (int)textA), 245, 158, 11), 5);
            TextOut(g, "$ " + amt, pf, Color.FromArgb(Math.Min(255, (int)textA), 253, 230, 138), pill, centered: true);
            tx += aw + 6 + gap;
        }

        // первая строка текста — продолжение шапки; остальное переносится
        var bodyColor = ScaleAlpha(Color.FromArgb(255, 236, 238, 246), textA);
        if (!string.IsNullOrWhiteSpace(m.Text))
        {
            var bodyFont = GetFont(fs, bold: false);
            int firstWidth = Math.Max(40, x + width - tx - 9);
            int restWidth = Math.Max(40, width - 18);
            var lines = WrapInline(g, m.Text, bodyFont, firstWidth, restWidth, 2);
            int lineY = ty;
            int lineX = tx;
            foreach (var line in lines)
            {
                if (lineY + (int)(fs * 1.38) > y + height - padV) break;
                DrawSeg(g, line, bodyFont, bodyColor, lineX, lineY, fs);
                lineY += (int)(fs * 1.38);
                lineX = x + 9;
            }
        }
    }

    /// Перенос текста: первая строка уже (рядом с шапкой), остальные — во всю ширину.
    private List<string> WrapInline(Graphics g, string text, Font font, int firstWidth, int restWidth, int maxLines)
    {
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var lines = new List<string>();
        string rest = text.Trim();
        if (string.IsNullOrEmpty(rest)) return lines;

        if (TextRenderer.MeasureText(g, rest, font, new Size(firstWidth, 4096), flags).Width <= firstWidth)
        {
            rest = "";
            lines.Add(text.Trim());
        }
        else
        {
            bool singleSlot = maxLines == 1;
            int cut = FitChars(g, rest, font, firstWidth, singleSlot ? "…" : "");
            int wordCut = cut;
            if (!singleSlot)
            {
                int sp = rest[..cut].LastIndexOf(' ');
                if (sp > 12) wordCut = sp;
            }
            lines.Add(singleSlot
                ? rest[..wordCut].TrimEnd('.', '…').TrimEnd() + "…"
                : rest[..wordCut].TrimEnd());
            rest = rest[wordCut..].Trim();
        }
        // остальные строки — уже на свободную ширину
        lines.AddRange(WrapText(g, rest, font, restWidth, maxLines - 1));
        return lines;
    }

    /// Общий перенос по словам; последняя строка усекается с многоточием.
    private List<string> WrapText(Graphics g, string text, Font font, int maxWidth, int maxLines)
    {
        var result = new List<string>();
        string rest = text.Trim();
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

        while (!string.IsNullOrEmpty(rest) && result.Count < maxLines)
        {
            if (TextRenderer.MeasureText(g, rest, font, new Size(maxWidth, 4096), flags).Width <= maxWidth)
            {
                result.Add(rest);
                rest = "";
                break;
            }
            bool last = result.Count == maxLines - 1;
            int cut = FitChars(g, rest, font, maxWidth, last ? "…" : "");
            // предпочитаем перенос по границе слова
            if (!last)
            {
                var sp = rest[..cut].LastIndexOf(' ');
                if (sp > 12) cut = sp;
            }
            var piece = rest[..cut].TrimEnd();
            result.Add(last ? piece.TrimEnd('.', '…') + "…" : piece);
            rest = rest[cut..].Trim();
        }
        return result;
    }

    /// Сколько первых символов текста помещается в maxWidth (бинарный поиск).
    private static int FitChars(Graphics g, string text, Font font, int maxWidth, string suffix)
    {
        int lo = 1, hi = text.Length, best = 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            var w = TextRenderer.MeasureText(g, text[..mid] + suffix, font, new Size(maxWidth + 1, 4096),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            if (w <= maxWidth) { best = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return best;
    }

    /// Узнаваемый мини-глиф площадки в цветном чипе (вектор, не подпись).
    private int DrawPlatformGlyph(Graphics g, string platform, Rectangle chip, byte textA)
    {
        var baseColor = ScaleAlpha(PlatformColor(platform), textA);
        FillRounded(g, chip, baseColor, 4);

        int inset = Math.Max(2, chip.Width / 6);
        var inner = new Rectangle(chip.X + inset, chip.Y + inset, chip.Width - inset * 2, chip.Height - inset * 2);
        byte maxA = textA;
        using var white = new SolidBrush(Color.FromArgb(Math.Min(255, (int)maxA), 255, 255, 255));
        using var whitePen = new Pen(Color.FromArgb(Math.Min(255, (int)maxA), 255, 255, 255), Math.Max(1f, chip.Width / 14f));

        switch (platform)
        {
            case "twitch":
                // фирменные «глаза» Twitch
                int eyeW = Math.Max(2, inner.Width / 5);
                g.FillRectangle(white, inner.X, inner.Y, eyeW, inner.Height);
                g.FillRectangle(white, inner.Right - eyeW, inner.Y, eyeW, inner.Height);
                break;
            case "youtube":
                // треугольник воспроизведения
                g.FillPolygon(white, new[]
                {
                    new Point(inner.X, inner.Y),
                    new Point(inner.X, inner.Bottom),
                    new Point(inner.Right, inner.Y + inner.Height / 2),
                });
                break;
            case "tiktok":
                // стилизованная нота-дуга
                g.DrawArc(whitePen, inner.X + 1, inner.Y + 1, inner.Width - 2, inner.Height - 2, 200, 300);
                break;
            case "donationalerts":
                // знак валюты
                TextOut(g, "$", GetFont(Math.Max(8, chip.Width - 6), bold: true),
                    Color.FromArgb(Math.Min(255, (int)maxA), 255, 255, 255), chip, centered: true);
                break;
            default:
                // VK/Kick/прочие — короткий тег
                TextOut(g, PlatformShort(platform), GetFont(Math.Max(7, (int)(chip.Width * 0.5)), bold: true),
                    Color.FromArgb(Math.Min(255, (int)maxA), 255, 255, 255), chip, centered: true);
                break;
        }
        return chip.Width;
    }

    private void StrokeRounded(Graphics g, Rectangle r, Color color, int radius)
    {
        if (r.Width < 1 || r.Height < 1) return;
        using var path = RoundedPath(r, Math.Clamp(radius, 0, Math.Min(r.Width, r.Height) / 2));
        using var pen = new Pen(color, 1f);
        g.DrawPath(pen, path);
    }

    private void DrawStatsBar(Graphics g, int x, int y, int width, int height, List<PlatformStat> shown)
    {
        byte textA = Alpha(_cfg.TextOpacity);
        if (_cfg.BackdropOpacity > 0.02)
            FillRounded(g, new Rectangle(x, y, width, height),
                Color.FromArgb(Math.Min(255, (int)((_cfg.BackdropOpacity + 0.06) * 255)), 8, 10, 16), _cfg.Radius);

        int fs = Math.Max(10, _cfg.FontSize - 1);
        int tx = x + 8;

        // донаты первыми
        if (_cfg.ShowDonations)
        {
            string s = $"{_donationsTotal:0} {_donationsCurrency}";
            if (!_cfg.StatsCompact && _donationsCount > 0) s += $" · {_donationsCount}";
            tx += DrawSeg(g, s, GetFont(fs, bold: true), ScaleAlpha(Color.FromArgb(255, 255, 180, 84), textA), tx, y, fs, height) + 8;
            if (shown.Count > 0)
            {
                using var pen = new Pen(ScaleAlpha(Color.FromArgb(255, 255, 255, 255), 64, textA));
                g.DrawLine(pen, tx - 4, y + height * 0.3f, tx - 4, y + height * 0.7f);
            }
        }

        foreach (var p in shown)
        {
            var chip = new Rectangle(tx, y + Math.Max(0, (height - (int)(fs * 1.3)) / 2), (int)(fs * 1.3), (int)(fs * 1.3));
            int chipW = DrawPlatformGlyph(g, p.Platform, chip, textA);
            tx += chipW + 3;
            if (_cfg.ShowViewers && p.Online && p.Viewers > 0)
                tx += DrawSeg(g, FormatViewers(p.Viewers), GetFont(fs, bold: false), ScaleAlpha(Color.FromArgb(255, 210, 214, 230), textA), tx, y, fs, height) + 6;
            else
                tx += 4;
        }
    }

    private void DrawHint(Graphics g, int x, int y, int fs)
    {
        var height = Math.Max(16, (int)(fs * 1.9));
        FillRounded(g, new Rectangle(x, y, Math.Max(150, Width - x * 2), height),
            Color.FromArgb(Math.Min(255, (int)(_cfg.BackdropOpacity * 255)), 8, 10, 16), _cfg.Radius);
        TextOut(g, "оверлей активен — сообщения появятся здесь", GetFont((int)(fs * 0.85), bold: false),
            ScaleAlpha(Color.FromArgb(255, 255, 255, 255), 166), new Rectangle(x, y, Width - x * 2, height), centered: true);
    }

    private void DrawDragHandle(Graphics g, int x, int y, int fs)
    {
        int hh = Math.Max(14, (int)(fs * 1.3));
        int hw = (int)(fs * 6.6);
        FillRounded(g, new Rectangle(x, y, hw, hh), Color.FromArgb(235, 139, 92, 246), 5);
        using var dot = new SolidBrush(Color.FromArgb(220, 255, 255, 255));
        for (int r = 0; r < 2; r++)
            for (int cdx = 0; cdx < 4; cdx++)
            {
                var cx = x + 6 + cdx * 5;
                var cy = y + hh / 2 - 4 + r * 5;
                g.FillEllipse(dot, cx, cy, 3, 3);
            }
        TextOut(g, "тяните", GetFont(Math.Max(9, (int)(fs * 0.75)), bold: true),
            Color.FromArgb(235, 255, 255, 255), new Rectangle(x + 26, y, hw - 26, hh), centered: false);
    }

    /* ---------- утилиты рисования ---------- */

    /// Рисует текст с необязательной тенью и многопараметрическим усечением.
    private int DrawSeg(Graphics g, string s, Font font, Color color, int x, int y, int fs, int maxWidth = -1, int height = -1)
    {
        if (height < 0) height = (int)(fs * 1.55);
        var rect = new Rectangle(x, y + (int)(fs * 0.05), maxWidth > 0 ? maxWidth : Width - x, height);
        return TextOut(g, s, font, color, rect, centered: false);
    }

    private int TextOut(Graphics g, string s, Font font, Color color, Rectangle rect, bool centered)
    {
        var flags = (centered ? TextFormatFlags.HorizontalCenter : 0)
                    | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                    | TextFormatFlags.EndEllipsis;
        if (_cfg.ShadowText && !centered)
            TextRenderer.DrawText(g, s, font, new Point(rect.X + 1, rect.Y + 1),
                Color.FromArgb(Math.Min(255, (int)color.A), 0, 0, 0), flags);
        TextRenderer.DrawText(g, s, font, rect, color, flags);
        return TextRenderer.MeasureText(s, font).Width;
    }

    private void FillRounded(Graphics g, Rectangle r, Color color, int radius)
    {
        if (r.Width < 1 || r.Height < 1) return;
        using var path = RoundedPath(r, Math.Clamp(radius, 0, Math.Min(r.Width, r.Height) / 2));
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    private static GraphicsPath RoundedPath(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        if (radius < 2) { p.AddRectangle(r); return p; }
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private Font GetFont(int size, bool bold)
    {
        size = Math.Clamp(size, 7, 48);
        var key = $"{size}:{(bold ? 1 : 0)}";
        if (_fonts.TryGetValue(key, out var f)) return f;
        // проверенные системные гарнитуры — работают на любой Windows,
        // без загрузки web-шрифтов (рендер нативный)
        var families = _cfg.FontFamily switch
        {
            "oswald" => new[] { "Oswald", "Arial Narrow", "Arial" },
            "jetbrains" => new[] { "JetBrains Mono", "Consolas", "Courier New" },
            "nunito" => new[] { "Nunito", "Segoe UI", "Arial" },
            _ => new[] { "Inter", "Segoe UI", "Arial" },
        };
        foreach (var name in families)
        {
            try
            {
                f = new Font(name, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
                _fonts[key] = f;
                return f;
            }
            catch { /* шрифта нет в системе — следующий */ }
        }
        f = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        _fonts[key] = f;
        return f;
    }

    /* ---------- утилиты данных ---------- */

    private static Color PlatformColor(string id) => id switch
    {
        "twitch" => Color.FromArgb(255, 145, 70, 255),
        "youtube" => Color.FromArgb(255, 255, 0, 51),
        "vkplay" => Color.FromArgb(255, 0, 119, 255),
        "kick" => Color.FromArgb(255, 83, 252, 24),
        "tiktok" => Color.FromArgb(255, 254, 44, 85),
        "donationalerts" => Color.FromArgb(255, 245, 125, 7),
        _ => Color.FromArgb(255, 139, 92, 246),
    };

    private static string PlatformShort(string id) => id switch
    {
        "twitch" => "TV",
        "youtube" => "YT",
        "vkplay" => "VK",
        "kick" => "KK",
        "tiktok" => "TT",
        "donationalerts" => "DA",
        _ => "?",
    };

    private static string FormatViewers(int v) => v switch
    {
        >= 1_000_000 => $"{v / 100_000 / 10.0:0.#}M",
        >= 10_000 => $"{v / 1000}K",
        >= 1_000 => $"{v / 100 / 10.0:0.#}K",
        _ => v.ToString(),
    };

    private static Color ParseColor(string hex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return Color.FromArgb(255, 240, 240, 246);
            var s = hex.Trim().TrimStart('#');
            if (s.Length == 3)
                s = string.Concat(s.Select(ch => new string(ch, 2)));
            if (s.Length == 6)
                return Color.FromArgb(255,
                    Convert.ToInt32(s[..2], 16), Convert.ToInt32(s.Substring(2, 2), 16), Convert.ToInt32(s.Substring(4, 2), 16));
        }
        catch { }
        return Color.FromArgb(255, 240, 240, 246);
    }

    private static byte Alpha(double opacity) => (byte)Math.Clamp((int)(opacity * 255), 0, 255);

    private static Color ScaleAlpha(Color c, byte alpha) =>
        Color.FromArgb(Math.Min(c.A, alpha), c.R, c.G, c.B);

    private static Color ScaleAlpha(Color c, int extra, byte alpha) =>
        Color.FromArgb(Math.Min(Math.Min((int)c.A, extra), (int)alpha), c.R, c.G, c.B);

    private static bool Bool(JsonObject o, string key, out bool v)
    {
        v = false;
        return o.TryGetPropertyValue(key, out var n) && n is JsonValue j && j.TryGetValue<bool>(out var b) && (v = b) == b;
    }

    private static bool Str(JsonObject o, string key, out string v)
    {
        v = "";
        if (o.TryGetPropertyValue(key, out var n) && n is JsonValue j && j.TryGetValue<string>(out var s) && s != null)
        {
            v = s;
            return true;
        }
        return false;
    }

    private static int Int(JsonObject o, string key, int fallback) =>
        o.TryGetPropertyValue(key, out var n) && n is JsonValue v && v.TryGetValue<double>(out var d)
            ? (int)Math.Round(d)
            : fallback;

    private static double Dbl(JsonObject o, string key, double fallback) =>
        o.TryGetPropertyValue(key, out var n) && n is JsonValue v && v.TryGetValue<double>(out var d) ? d : fallback;

    private static string TryString(JsonElement e, string key, string fallback) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;

    private static double TryDouble(JsonElement e, string key, double fallback) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : fallback;

    private static bool TryBool(JsonElement e, string key, bool fallback) =>
        e.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;

    /* ================= GDI/ULW ================= */

    private void EnsureGdi()
    {
        if (_hdcScreen == IntPtr.Zero) _hdcScreen = GetDC(IntPtr.Zero);
        if (_hdcMem == IntPtr.Zero) _hdcMem = CreateCompatibleDC(_hdcScreen);
    }

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
            biHeight = -h,          // top-down
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,
        };
        _dib = CreateDIBSection(_hdcMem, ref bmi, 0, out _dibPtr, IntPtr.Zero, 0);
        SelectObject(_hdcMem, _dib);
        _pixels = new byte[w * h * 4];
        Marshal.Copy(_pixels, 0, _dibPtr, _pixels.Length);
        _bufW = w;
        _bufH = h;
    }

    private void PresentFrame()
    {
        if (!IsHandleCreated || _dib == IntPtr.Zero || _bufW < 2 || _bufH < 2) return;
        var dst = new POINT { X = Left, Y = Top };
        var size = new SIZE { cx = _bufW, cy = _bufH };
        var src = new POINT { X = 0, Y = 0 };
        var blend = new BLENDFUNCTION
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1,
        };
        try { UpdateLayeredWindow(Handle, _hdcScreen, ref dst, ref size, _hdcMem, ref src, 0, ref blend, ULW_ALPHA); }
        catch { }
    }

    private void PresentBlank(int w, int h)
    {
        if (!IsHandleCreated || w < 2 || h < 2) return;
        EnsureGdi();
        EnsureDib(w, h);
        Array.Clear(_pixels!, 0, _pixels!.Length);
        Marshal.Copy(_pixels, 0, _dibPtr, _pixels.Length);
        PresentFrame();
    }

    private byte PixelAlphaAtScreen(int screenX, int screenY)
    {
        var p = _pixels;
        if (p == null || _bufW < 1 || _bufH < 1) return 0;
        int x = screenX - Left;
        int y = screenY - Top;
        if (x < 0 || y < 0 || x >= _bufW || y >= _bufH) return 0;
        return p[(y * _bufW + x) * 4 + 3];
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_ERASEBKGND) { m.Result = (IntPtr)1; return; }
        if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
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

    private void DragTick()
    {
        if (!_freePosition || _locked || _clickThrough) { _dragging = false; return; }
        if (!GetCursorPos(out var p)) return;
        var cursor = new Point(p.X, p.Y);
        bool pressed = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

        if (!_dragging)
        {
            // схватить окно можно только над видимым пикселем (ручка/контент)
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
            PositionChanged?.Invoke(Left, Top);
        }
    }

    public void ForceClose() { FormClosing -= null; Dispose(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _dragTimer.Dispose(); } catch { }
            try { _fadeTimer.Dispose(); } catch { }
            foreach (var f in _fonts.Values) { try { f.Dispose(); } catch { } }
            _fonts.Clear();
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

            // ВСЕ страницы виджет-сервера прозрачны изначально (!important перекрывает
            // ранний тёмный фон), а любой «голый» адрес насильно ведёт в виджет —
            // это спасает, если источник в OBS добавлен без суффикса /widget.
            const string transparentStyle =
                "<style id=\"yawa-widget-transparent\">" +
                "html,body,#root,#boot{background:transparent!important;background-color:transparent!important}" +
                "</style>";
            var needsWidgetRoute = path == "/" || path.StartsWith("/index") || path.StartsWith("/widget");
            var html = Encoding.UTF8.GetString(bytes);
            var injection = transparentStyle + (needsWidgetRoute
                ? "<script>if(!location.hash.startsWith('#/widget'))location.hash='#/widget'+location.search</script>"
                : "");
            html = html.Replace("</head>", injection + "</head>");
            bytes = Encoding.UTF8.GetBytes(html);

            ctx.Response.ContentType = "text/html; charset=utf-8";
            // OBS не должен держать старый index.html после обновления exe.
            ctx.Response.Headers.Add("Cache-Control", "no-store, no-cache, must-revalidate");
            ctx.Response.Headers.Add("Pragma", "no-cache");
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

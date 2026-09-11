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
/// Игровой оверлей: старый React-интерфейс #/overlay пиксель-в-пиксель.
///
/// WebView2 рендерит страницу в невидимом служебном окне. Прозрачность не
/// берётся из WebView2 (CapturePreview отдаёт transparent как чёрный): хост
/// снимает страницу дважды — на чёрном и белом фоне — и математически
/// восстанавливает исходный alpha-канал:
///   black = alpha * foreground
///   white = alpha * foreground + (1-alpha) * 255
///   alpha = 255 - (white-black)
/// Полученный premultiplied BGRA выводится через UpdateLayeredWindow.
/// Поэтому визуал полностью старый/вебовый, а прозрачность не зависит от
/// Windows, DWM, DirectComposition или версии Evergreen Runtime.
/// </summary>
public sealed class OverlayWindow : Form
{
    private readonly HostApi _api;
    private readonly RenderHost _renderHost = new();
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };

    private bool _clickThrough;
    private bool _freePosition = true;
    private bool _locked;
    private JsonObject? _pendingConfig;
    private JsonNode? _lastStats;
    private bool _webReady;

    /* события рендереру, пришедшие до NavigationCompleted */
    private readonly object _eventGate = new();
    private readonly Queue<string> _pendingEvents = new();

    /* действия над Form, пришедшие до создания HWND (исправляет BeginInvoke exception) */
    private readonly object _uiGate = new();
    private readonly Queue<Action> _pendingUi = new();

    /* перетаскивание */
    private readonly System.Windows.Forms.Timer _dragTimer = new() { Interval = 15 };
    private bool _dragging;
    private Point _grabOffset;

    /* съёмка: DOM сам сообщает overlay.dirty; таймер — страховка для autoFade */
    private readonly System.Windows.Forms.Timer _captureTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _captureDebounce = new() { Interval = 70 };
    private bool _capturing;
    private bool _captureAgain;

    /* GDI / per-pixel alpha */
    private IntPtr _hdcScreen;
    private IntPtr _hdcMem;
    private IntPtr _dib;
    private IntPtr _oldBitmap;
    private IntPtr _dibPtr;
    private byte[]? _pixels;
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
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000,
                      WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8, WS_EX_NOACTIVATE = 0x8000000;
    private const int VK_LBUTTON = 0x01;
    private const uint ULW_ALPHA = 0x2;
    private const int WM_NCHITTEST = 0x84, WM_ERASEBKGND = 0x14, WM_MOUSEACTIVATE = 0x21;
    private const int MA_NOACTIVATE = 3, HTCLIENT = 1, HTTRANSPARENT = -1;
    private const uint SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_FRAMECHANGED = 0x20;
    private const byte AlphaHitThreshold = 12;

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
    protected override void OnPaint(PaintEventArgs e) => PresentFrame();

    public OverlayWindow(HostApi api)
    {
        _api = api;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        var scr = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Bounds = new Rectangle(scr.Right - 420, scr.Top + 24, 400, scr.Height / 2);
        BackColor = Color.Black; // окно рисуется только UpdateLayeredWindow

        _renderHost.Controls.Add(_web);
        _renderHost.Size = Size;

        _dragTimer.Tick += (_, _) => DragTick();
        _captureTimer.Tick += (_, _) => ScheduleCapture();
        _captureDebounce.Tick += async (_, _) =>
        {
            _captureDebounce.Stop();
            await CaptureOnceAsync();
        };
        VisibleChanged += (_, _) =>
        {
            if (Visible) { PresentBlank(Width, Height); ScheduleCapture(); }
        };
        Load += async (_, _) => await InitRenderer();
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
    }

    /// Очередь до создания дескриптора — Apply вызывается из EnsureOverlay ДО Show().
    private void RunUi(Action action)
    {
        if (IsDisposed) return;
        if (!IsHandleCreated)
        {
            lock (_uiGate) _pendingUi.Enqueue(action);
            return;
        }
        if (InvokeRequired)
        {
            try { BeginInvoke(action); } catch { }
        }
        else action();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Action[] queued;
        lock (_uiGate)
        {
            queued = _pendingUi.ToArray();
            _pendingUi.Clear();
        }
        foreach (var action in queued)
        {
            try { action(); } catch { }
        }
        UpdateExStyle();
    }

    private async Task InitRenderer()
    {
        if (_web.CoreWebView2 != null) return;
        _renderHost.Size = Size;
        _renderHost.Show();

        await _web.EnsureCoreWebView2Async(await Program.WebViewEnvAsync());
        _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
        _web.CoreWebView2.Settings.IsZoomControlEnabled = false;

        // Холст старого оверлея обязан оставаться прозрачным; переходные
        // CSS-анимации отключены, чтобы два снимка были идентичны по геометрии.
        await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("""
            (() => {
              const install = () => {
                if (document.getElementById('yawa-overlay-capture')) return;
                const parent = document.head || document.documentElement;
                if (!parent) return;
                const s = document.createElement('style');
                s.id = 'yawa-overlay-capture';
                s.textContent = 'html,body,#root,#boot{background:transparent!important;background-color:transparent!important}*{animation:none!important;transition:none!important}';
                parent.appendChild(s);
              };
              document.addEventListener('DOMContentLoaded', install, { once: true });
              try { install(); } catch(e) {}
              setTimeout(install, 0);
              let t = 0;
              const dirty = () => { clearTimeout(t); t = setTimeout(() => { try { chrome.webview.postMessage({method:'overlay.dirty'}); } catch(e) {} }, 50); };
              const observe = () => { try { new MutationObserver(dirty).observe(document.documentElement,{subtree:true,childList:true,attributes:true,characterData:true}); } catch(e) {} };
              document.addEventListener('DOMContentLoaded', observe, { once:true });
            })();
            """);

        _web.CoreWebView2.WebMessageReceived += (_, e) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;
                var method = root.GetProperty("method").GetString() ?? "";
                if (method == "overlay.dirty") { ScheduleCapture(); return; }
                int id = root.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
                var args = root.TryGetProperty("args", out var a)
                    ? a.EnumerateArray().Select(x => (object?)x.Clone()).ToArray()
                    : Array.Empty<object?>();
                _ = _api.DispatchAsync(id, method, args, Reply);
            }
            catch { }
        };

        _web.CoreWebView2.NavigationCompleted += (_, _) =>
        {
            _webReady = true;
            if (_pendingConfig != null) Post("overlay.config", _pendingConfig);
            if (_lastStats != null) Post("overlay.stats", _lastStats);

            string[] queued;
            lock (_eventGate)
            {
                queued = _pendingEvents.ToArray();
                _pendingEvents.Clear();
            }
            foreach (var json in queued) PostJson(json);
            _captureTimer.Start();
            ScheduleCapture();
        };

        _web.CoreWebView2.Navigate(Program.RendererUrl(false, "/overlay"));
    }

    public void Reply(int id, bool ok, object? result, string? error)
    {
        var json = JsonSerializer.Serialize(new { id, ok, result, error });
        RunUi(() => PostJson(json));
    }

    /* ================= старый renderer ↔ host ================= */

    public void Apply(JsonNode? cfg)
    {
        if (cfg is not JsonObject o) return;
        _pendingConfig = (JsonObject)o.DeepClone();
        // Никакого BeginInvoke до HWND: RunUi ставит действие в очередь.
        RunUi(() => ApplyNow((JsonObject)o.DeepClone()));
    }

    private void ApplyNow(JsonObject o)
    {
        if (o.TryGetPropertyValue("clickThrough", out var ct) && ct is JsonValue cv && cv.TryGetValue<bool>(out var b))
            _clickThrough = b;

        string corner = ReadString(o, "corner", "top-left");
        int width = ReadInt(o, "width", 400);
        int offX = ReadInt(o, "offsetX", 16);
        int offY = ReadInt(o, "offsetY", 16);
        int fontSize = ReadInt(o, "fontSize", 14);
        int maxMsg = ReadInt(o, "maxMessages", 6);
        bool showStats = ReadBool(o, "showStats", true);
        _freePosition = ReadBool(o, "freePosition", true);
        _locked = ReadBool(o, "locked", false);

        var scr = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        int height = Math.Min(scr.Height - 40, (int)((maxMsg + (showStats ? 1.4 : 0)) * (fontSize * 2.35) + 28));
        height = Math.Max(90, height);
        width = Math.Clamp(width, 240, scr.Width);

        if (_freePosition)
        {
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

        _renderHost.Size = new Size(Width, Height);
        SetMovable(_freePosition && !_locked && !_clickThrough);
        UpdateExStyle();
        PresentBlank(Width, Height);
        if (_webReady) Post("overlay.config", _pendingConfig!);
        ScheduleCapture();
    }

    public void SetStats(JsonNode? stats)
    {
        _lastStats = stats?.DeepClone();
        if (_lastStats != null) Forward("overlay.stats", _lastStats);
    }

    public void Forward(string type, object payload)
    {
        var json = JsonSerializer.Serialize(new { @event = type, payload });
        lock (_eventGate)
        {
            if (!_webReady)
            {
                _pendingEvents.Enqueue(json);
                while (_pendingEvents.Count > 100) _pendingEvents.Dequeue();
                return;
            }
        }
        RunUi(() => PostJson(json));
    }

    private void Post(string type, object payload) =>
        PostJson(JsonSerializer.Serialize(new { @event = type, payload }));

    private void PostJson(string json)
    {
        try { _web.CoreWebView2?.PostWebMessageAsJson(json); } catch { }
    }

    /* ================= alpha reconstruction ================= */

    private void ScheduleCapture()
    {
        RunUi(() =>
        {
            if (!Visible || !_webReady) return;
            if (_capturing) { _captureAgain = true; return; }
            _captureDebounce.Stop();
            _captureDebounce.Start();
        });
    }

    private async Task CaptureOnceAsync()
    {
        if (_capturing || !Visible || !_webReady) return;
        _capturing = true;
        try
        {
            using var black = await CaptureOnAsync(Color.Black);
            using var white = await CaptureOnAsync(Color.White);
            RecoverAlpha(black, white);
            PresentFrame();
        }
        catch { /* следующий DOM-event/таймер повторит */ }
        finally
        {
            _capturing = false;
            if (_captureAgain)
            {
                _captureAgain = false;
                ScheduleCapture();
            }
        }
    }

    private async Task<Bitmap> CaptureOnAsync(Color background)
    {
        _web.DefaultBackgroundColor = background;
        // Два visual-frame + короткая страховка: фон контроллера успевает
        // попасть в композитинг и на медленных/удалённых GPU.
        await _web.CoreWebView2.ExecuteScriptAsync("new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)))");
        await Task.Delay(18);

        using var ms = new MemoryStream();
        await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
        ms.Position = 0;
        using var image = Image.FromStream(ms);

        // CapturePreview может вернуть physical-pixel размер при DPI > 100%;
        // приводим оба кадра к ClientSize, иначе ULW меняет геометрию окна.
        int w = Math.Max(2, _renderHost.ClientSize.Width);
        int h = Math.Max(2, _renderHost.ClientSize.Height);
        var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, new Rectangle(0, 0, w, h));
        }
        return result;
    }

    private void RecoverAlpha(Bitmap black, Bitmap white)
    {
        int w = Math.Min(black.Width, white.Width);
        int h = Math.Min(black.Height, white.Height);
        if (w < 2 || h < 2) return;

        var b = ReadBgra(black, w, h);
        var q = ReadBgra(white, w, h);
        var output = new byte[w * h * 4];

        for (int i = 0; i < output.Length; i += 4)
        {
            // Все три разницы теоретически равны (255-alpha); медиана
            // устойчива к субпиксельному сглаживанию текста.
            int d0 = Math.Clamp(q[i] - b[i], 0, 255);
            int d1 = Math.Clamp(q[i + 1] - b[i + 1], 0, 255);
            int d2 = Math.Clamp(q[i + 2] - b[i + 2], 0, 255);
            int delta = Median(d0, d1, d2);
            int a = 255 - delta;
            if (a <= 2) { output[i] = output[i + 1] = output[i + 2] = output[i + 3] = 0; continue; }

            // Снимок на чёрном уже содержит premultiplied RGB = alpha*foreground.
            output[i] = (byte)Math.Min(a, b[i]);
            output[i + 1] = (byte)Math.Min(a, b[i + 1]);
            output[i + 2] = (byte)Math.Min(a, b[i + 2]);
            output[i + 3] = (byte)a;
        }

        EnsureGdi();
        EnsureDib(w, h);
        _pixels = output;
        Marshal.Copy(output, 0, _dibPtr, output.Length);
    }

    private static int Median(int a, int b, int c) =>
        a > b ? (b > c ? b : Math.Min(a, c)) : (a > c ? a : Math.Min(b, c));

    private static byte[] ReadBgra(Bitmap bmp, int w, int h)
    {
        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var result = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, result, y * w * 4, w * 4);
            return result;
        }
        finally { bmp.UnlockBits(data); }
    }

    /* ================= GDI / window ================= */

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
            if (_oldBitmap != IntPtr.Zero) SelectObject(_hdcMem, _oldBitmap);
            DeleteObject(_dib);
            _dib = IntPtr.Zero;
        }
        var bmi = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,
        };
        _dib = CreateDIBSection(_hdcMem, ref bmi, 0, out _dibPtr, IntPtr.Zero, 0);
        _oldBitmap = SelectObject(_hdcMem, _dib);
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
        var src = new POINT();
        var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
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

    private byte PixelAlphaAtScreen(int sx, int sy)
    {
        var p = _pixels;
        if (p == null || _bufW < 1 || _bufH < 1) return 0;
        int x = sx - Left, y = sy - Top;
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
            m.Result = PixelAlphaAtScreen(sx, sy) >= AlphaHitThreshold ? (IntPtr)HTCLIENT : (IntPtr)HTTRANSPARENT;
            return;
        }
        base.WndProc(ref m);
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

    private void DragTick()
    {
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
            PresentFrame();
        }
        else
        {
            _dragging = false;
            PositionChanged?.Invoke(Left, Top);
        }
    }

    protected override bool ShowWithoutActivation => true;

    public void CenterOnScreen()
    {
        RunUi(() =>
        {
            var scr = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            Location = new Point(scr.Left + (scr.Width - Width) / 2, scr.Top + (scr.Height - Height) / 2);
            PositionChanged?.Invoke(Left, Top);
            PresentFrame();
        });
    }

    public event Action<int, int>? PositionChanged;

    private void UpdateExStyle()
    {
        if (!IsHandleCreated) return;
        int ex = GetWindowLong(Handle, GWL_EXSTYLE);
        ex |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
        ex = _clickThrough ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetWindowLong(Handle, GWL_EXSTYLE, ex);
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        PresentFrame();
    }

    private static int ReadInt(JsonObject o, string key, int fallback) =>
        o.TryGetPropertyValue(key, out var n) && n is JsonValue v && v.TryGetValue<double>(out var d)
            ? (int)Math.Round(d) : fallback;
    private static bool ReadBool(JsonObject o, string key, bool fallback) =>
        o.TryGetPropertyValue(key, out var n) && n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;
    private static string ReadString(JsonObject o, string key, string fallback) =>
        o.TryGetPropertyValue(key, out var n) && n is JsonValue v && v.TryGetValue<string>(out var s) && s != null ? s : fallback;

    public void ForceClose() => Dispose();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _captureTimer.Dispose(); } catch { }
            try { _captureDebounce.Dispose(); } catch { }
            try { _dragTimer.Dispose(); } catch { }
            try { _renderHost.Dispose(); } catch { }
        }
        if (_dib != IntPtr.Zero)
        {
            if (_hdcMem != IntPtr.Zero && _oldBitmap != IntPtr.Zero) SelectObject(_hdcMem, _oldBitmap);
            DeleteObject(_dib);
            _dib = IntPtr.Zero;
        }
        if (_hdcMem != IntPtr.Zero) { DeleteDC(_hdcMem); _hdcMem = IntPtr.Zero; }
        if (_hdcScreen != IntPtr.Zero) { ReleaseDC(IntPtr.Zero, _hdcScreen); _hdcScreen = IntPtr.Zero; }
        base.Dispose(disposing);
    }

    /// Невидимый WebView-хост старого UI; остаётся on-screen для Chromium,
    /// но никогда не забирает фокус/мышь и не виден пользователю.
    private sealed class RenderHost : Form
    {
        public RenderHost()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Opacity = 0;
            var scr = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            Bounds = new Rectangle(scr.Right - 410, scr.Bottom - 310, 400, 300);
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
                return cp;
            }
        }
    }
}

/// <summary>
/// Сервер виджета для OBS Browser Source: отдаёт #/widget и SSE-события.
/// HTML прозрачен ДО запуска React и не кешируется OBS.
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
        catch { }
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
                if (_lastConfig != null) SendTo(ctx.Response, "widget.config", _lastConfig);
                return;
            }

            RendererFiles.EnsureExtracted();
            var local = Path.GetFullPath(Path.Combine(RendererFiles.Dir, "index.html"));
            if (!File.Exists(local))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }
            var bytes = await File.ReadAllBytesAsync(local);
            const string transparentStyle =
                "<style id=\"yawa-widget-transparent\">" +
                "html,body,#root,#boot{background:transparent!important;background-color:transparent!important}" +
                "</style>";
            var html = Encoding.UTF8.GetString(bytes);
            html = html.Replace("</head>", transparentStyle +
                "<script>if(!location.hash.startsWith('#/widget'))location.hash='#/widget'+location.search</script></head>");
            bytes = Encoding.UTF8.GetBytes(html);

            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers.Add("Cache-Control", "no-store, no-cache, must-revalidate");
            ctx.Response.Headers.Add("Pragma", "no-cache");
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
        catch { try { ctx.Response.Abort(); } catch { } }
    }

    private object? _lastConfig;
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

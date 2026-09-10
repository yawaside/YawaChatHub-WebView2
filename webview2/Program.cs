using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace YawaChatHub;

/// <summary>
/// Точка входа WebView2-оболочки. Заменяет desktop/electron/main.js:
/// то же безрамочное окно, тот же интерфейс (Vite-сборка → renderer/index.html),
/// трей, глобальные хоткеи, перетаскивание за кастомную шапку.
/// </summary>
internal static class Program
{
    public static readonly string AppDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YawaChatHub");

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Directory.CreateDirectory(AppDir);
        Directory.CreateDirectory(Path.Combine(AppDir, "webview2")); // user data folder WebView2
        Application.Run(new MainWindow());
    }

    /// <summary>
    /// ЕДИНОЕ окружение WebView2 для всех окон приложения.
    /// Без него каждое окно создаёт папку профиля РЯДОМ С EXE
    /// (YawaChatHub.exe.WebView2) — теперь все данные живут в AppData.
    /// </summary>
    private static Task<CoreWebView2Environment>? _envTask;
    public static Task<CoreWebView2Environment> WebViewEnvAsync()
    {
        return _envTask ??= CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: Path.Combine(AppDir, "webview2"),
            options: null);
    }

    /// Расположение интерфейса: встроенный в exe рендерер, извлечённый в AppDir,
    /// либо dev-сервер Vite при --dev.
    public static string RendererUrl(bool dev, string fragment = "/app")
    {
        if (dev) return $"http://localhost:5173/#{fragment}";
        return RendererFiles.IndexFileUri() + "#" + fragment;
    }
}

/// <summary>
/// Интерфейс встроен в exe как EmbeddedResource (Vite собирает единый index.html).
/// Перед первым запуском извлекаем его в %LOCALAPPDATA%\YawaChatHub\renderer\ —
/// благодаря этому zip с одним YawaChatHub.exe полностью самодостаточен.
/// </summary>
internal static class RendererFiles
{
    private const string ResourceName = "yawa-renderer-index.html";

    public static string Dir => Path.Combine(Program.AppDir, "renderer");
    public static string ExtractedPath => Path.Combine(Dir, "index.html");

    /// file://-URI через Uri.AbsoluteUri — корректно кодирует пробелы и кириллицу в пути профиля.
    public static string IndexFileUri() => new Uri(EnsureExtracted()).AbsoluteUri;

    public static string EnsureExtracted()
    {
        Directory.CreateDirectory(Dir);
        var target = ExtractedPath;

        using var res = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                "Интерфейс не встроен в exe: выполните 'npm run build' перед 'dotnet publish'.");
        using var ms = new MemoryStream();
        res.CopyTo(ms);
        var bytes = ms.ToArray();

        // перезаписываем только если сборка интерфейса изменилась (добавляем 1 байт-соль от размера)
        if (!File.Exists(target) || new FileInfo(target).Length != bytes.Length)
            File.WriteAllBytes(target, bytes);

        return target;
    }
}

public sealed class MainWindow : Form
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly HostApi _api;
    private readonly NotifyIcon _tray;
    private bool _closeToTray;
    private readonly bool _startHidden;

    // ---- Win32 ----
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Скругление окна: DWM на Windows 11, Region — запасной путь на Windows 10.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    /// Толщина полосы формы вокруг WebView2 — за неё тянется ресайз.
    private const int ResizeBorder = 6;
    private const int CornerRadius = 12;

    private const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 0x2, WM_HOTKEY = 0x0312, WM_NCHITTEST = 0x84;

    public MainWindow()
    {
        bool dev = Environment.GetCommandLineArgs().Contains("--dev");
        _startHidden = Environment.GetCommandLineArgs().Contains("--hidden");

        Text = "YawaChatHub";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1180, 760);
        MinimumSize = new Size(880, 560);
        FormBorderStyle = FormBorderStyle.None; // окно без системной рамки
        BackColor = Color.FromArgb(10, 11, 19);
        // WebView2 накрывает форму целиком и забирает мышь, поэтому оставляем
        // тонкую полосу самой формы по периметру — именно она ловит ресайз.
        Padding = new Padding(ResizeBorder);
        Controls.Add(_web);

        _api = new HostApi(this, _web);

        // трей (аналог electron.tray)
        _tray = new NotifyIcon
        {
            Text = "YawaChatHub — чат всех стримов",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };
        _tray.ContextMenuStrip.Items.Add("Показать", null, (_, _) => RestoreFromTray());
        _tray.ContextMenuStrip.Items.Add("Выход", null, (_, _) => { _closeToTray = false; Close(); });
        _tray.DoubleClick += (_, _) => RestoreFromTray();

        Load += async (_, _) => await InitWeb(dev);
        Shown += (_, _) => ApplyRoundedCorners();
        Resize += (_, _) => ApplyRoundedCorners();
        FormClosing += (_, e) =>
        {
            // «крестик» → трей, если включено в настройках интерфейса
            if (_closeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                _tray.Visible = false;
                _api.Dispose();
            }
        };
    }

    public void SetCloseToTray(bool v) => _closeToTray = v;

    /// <summary>Скруглённые углы окна.</summary>
    private void ApplyRoundedCorners()
    {
        if (!IsHandleCreated) return;

        if (WindowState == FormWindowState.Maximized)
        {
            Region = null;            // развёрнутое окно всегда прямоугольное
            Padding = new Padding(0); // и без полосы под ресайз
            return;
        }

        Padding = new Padding(ResizeBorder);

        var preference = DWMWCP_ROUND;
        var dwmOk = false;
        try
        {
            dwmOk = DwmSetWindowAttribute(
                Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int)) == 0;
        }
        catch { }

        if (dwmOk) { Region = null; return; }

        try
        {
            var rgn = CreateRoundRectRgn(0, 0, Width, Height, CornerRadius * 2, CornerRadius * 2);
            if (rgn != IntPtr.Zero) Region = System.Drawing.Region.FromHrgn(rgn);
        }
        catch { }
    }

    private async Task InitWeb(bool dev)
    {
        await _web.EnsureCoreWebView2Async(await Program.WebViewEnvAsync());

        var s = _web.CoreWebView2.Settings;
        s.AreDefaultContextMenusEnabled = dev;
        s.AreDefaultScriptDialogsEnabled = false;
        s.IsStatusBarEnabled = false;
        s.AreDevToolsEnabled = dev;
        s.IsZoomControlEnabled = false;

        // перетаскивание окна за кастомную шапку рендерера (.drag-region)
        await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("""
            document.addEventListener('mousedown', (e) => {
              if (e.button !== 0) return;
              const drag = e.target.closest && e.target.closest('.drag-region');
              if (!drag) return;
              if (e.target.closest('.no-drag, button, input, select, a, [role="button"]')) return;
              window.chrome.webview.postMessage({ method: 'window.drag' });
            });
        """);

        // ЗАЩИТА ОТ «ПУСТОГО ОКНА»: рендерер не должен никуда уходить со своей страницы.
        // Любая посторонняя навигация (submit формы, внешняя ссылка) отменяется,
        // http/https-адреса открываются в браузере по умолчанию.
        _web.CoreWebView2.NavigationStarting += (_, e) =>
        {
            var target = e.Uri ?? "";
            var allowed = target.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                          || target.StartsWith("http://localhost:5173", StringComparison.OrdinalIgnoreCase)
                          || target.StartsWith("about:", StringComparison.OrdinalIgnoreCase);
            if (allowed) return;
            e.Cancel = true;
            if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                try { System.Diagnostics.Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
        };
        _web.CoreWebView2.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (!string.IsNullOrEmpty(e.Uri))
                try { System.Diagnostics.Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true }); } catch { }
        };
        // если процесс рендерера упал — перезагружаем интерфейс, а не оставляем пустое окно
        _web.CoreWebView2.ProcessFailed += (_, _) =>
        {
            try { _web.CoreWebView2.Navigate(Program.RendererUrl(dev)); } catch { }
        };

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
                _ = _api.DispatchAsync(id, method, args);
            }
            catch { /* мусорные сообщения игнорируем */ }
        };

        _web.CoreWebView2.Navigate(Program.RendererUrl(dev));
        if (!_startHidden) { /* окно видимо по умолчанию */ }
        else BeginInvoke(() => Hide());
    }

    /// Ответ рендереру на JSON-RPC вызов (bridge.ts ждёт {id, ok, result}).
    public void Reply(int id, bool ok, object? result, string? error = null)
    {
        var payload = JsonSerializer.Serialize(new { id, ok, result, error });
        BeginInvoke(() =>
        {
            if (_web.CoreWebView2 != null) _web.CoreWebView2.PostWebMessageAsJson(payload);
        });
    }

    /// Событие от хоста к рендереру: hotkey, chat.message и т.п.
    public void SendEvent(string type, object? payload)
    {
        var json = JsonSerializer.Serialize(new { @event = type, payload });
        BeginInvoke(() =>
        {
            if (_web.CoreWebView2 != null) _web.CoreWebView2.PostWebMessageAsJson(json);
        });
    }

    public void BeginDrag()
    {
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    public void ToggleMaximize() =>
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;

    private void RestoreFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); }

    // глобальные хоткеи (аналог electron globalShortcut)
    private readonly Dictionary<int, string> _hotkeys = new();
    public void ApplyHotkeys(Dictionary<string, string> map)
    {
        foreach (var id in _hotkeys.Keys) UnregisterHotKey(Handle, id);
        _hotkeys.Clear();
        int i = 9000;
        foreach (var (action, combo) in map)
        {
            var (mod, vk) = KeyParser.Parse(combo);
            if (vk == 0) continue;
            if (RegisterHotKey(Handle, i, mod, vk)) _hotkeys[i] = action;
            i++;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && _hotkeys.TryGetValue(m.WParam.ToInt32(), out var action))
        {
            SendEvent("hotkey", action);
            return;
        }
        // ресайз безрамочного окна за края
        if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
        {
            base.WndProc(ref m);
            if (m.Result == (IntPtr)1) // HTCLIENT
            {
                const int grip = ResizeBorder + 2;
                var p = PointToClient(Cursor.Position);
                int hit = 1;
                if (p.Y <= grip) hit = p.X <= grip ? 13 : p.X >= Width - grip ? 14 : 12;   // top
                else if (p.Y >= Height - grip) hit = p.X <= grip ? 16 : p.X >= Width - grip ? 17 : 15; // bottom
                else if (p.X <= grip) hit = 10;  // left
                else if (p.X >= Width - grip) hit = 11; // right
                m.Result = (IntPtr)hit;
            }
            return;
        }
        base.WndProc(ref m);
    }

    private sealed class KeyParser
    {
        public static (uint mod, uint vk) Parse(string combo)
        {
            uint mod = 0, vk = 0;
            foreach (var part in combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": mod |= 0x0002; break;   // MOD_CONTROL
                    case "alt": mod |= 0x0001; break;    // MOD_ALT
                    case "shift": mod |= 0x0004; break;  // MOD_SHIFT
                    case "meta": case "win": mod |= 0x0008; break; // MOD_WIN
                    default:
                        if (part.StartsWith('F') && int.TryParse(part[1..], out int f) && f is >= 1 and <= 24)
                            vk = (uint)(0x70 + f - 1); // VK_F1
                        else
                            vk = (uint)char.ToUpperInvariant(part[0]); // VK букв/цифр
                        break;
                }
            }
            return (mod, vk);
        }
    }
}

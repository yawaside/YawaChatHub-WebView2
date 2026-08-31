using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using YawaChatHub.Services;

namespace YawaChatHub;

public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public static OverlayWindow? Instance { get; private set; }

    private readonly BridgeHost _bridge;
    private bool _init;

    public OverlayWindow()
    {
        Instance = this;
        _bridge = new BridgeHost { mode = "overlay" };
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;

        // Подписываемся на изменения настроек, чтобы ApplyConfig вызывался
        // и чтобы настройки (включая визуальные стили) отправлялись в WebView.
        SettingsService.Instance.Changed += s =>
        {
            ApplyConfig(s.Overlay);
            // Отправляем настройки в WebView оверлея, чтобы React-компонент
            // мог обновить стили (радиус, цвета и т.д.).
            _bridge.EmitSettings(s);
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        ApplyClickThrough(hwnd);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Instance.Current;
        var b = s.OverlayBounds;
        if (b != null)
        {
            Left = b.X; Top = b.Y; Width = b.W; Height = b.H;
        }
        else
        {
            // по умолчанию — правый верхний угол экрана (ТЗ §5.2)
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 24;
            Top = wa.Top + 24;
        }

        LocationChanged += (_, _) => SaveBounds();
        SizeChanged += (_, _) => SaveBounds();

        // Перетаскивание оверлея мышью (WebView2 не перехватывает MouseLeftButtonDown
        // на шапке, поэтому обрабатываем на уровне окна).
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left && !SettingsService.Instance.Current.Overlay.Locked)
            {
                DragMove();
            }
        };
    }

    public async void LoadPage()
    {
        if (_init) return;
        _init = true;
        try
        {
            WebAssets.EnsureExtracted();
            await WebView.EnsureCoreWebView2Async();
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "yawachat.invalid", WebAssets.AppFolder,
                CoreWebView2HostResourceAccessKind.Allow);
            WebView.CoreWebView2.AddHostObjectToScript("sp", _bridge);
            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            WebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.__YAWA_DESKTOP__=true;try{window.sp=chrome.webview.hostObjects.sync.sp;}catch(e){}");
            _bridge.Script = js => WebView.Dispatcher.Invoke(() =>
            {
                _ = WebView.CoreWebView2.ExecuteScriptAsync(js);
            });
            // Перетаскивание оверлея: React шлёт chrome.webview.postMessage, а мы
            // вызываем DragMove в главном потоке WPF (если окно не закреплено).
            WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _bridge.DragRequested += () =>
            {
                if (!SettingsService.Instance.Current.Overlay.Locked) DragMove();
            };
            WebView.CoreWebView2.Navigate("https://yawachat.invalid/index.html#/overlay");
        }
        catch (Exception ex)
        {
            _init = false;
            System.Windows.MessageBox.Show(
                WebAssets.DescribeFailure(ex), "YawaChatHub — оверлей",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Hide();
        }
    }

    public void Toggle()
    {
        if (IsVisible) { Hide(); }
        else { LoadPage(); Show(); Activate(); }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "window") return;
            if (!root.TryGetProperty("action", out var action)) return;
            switch (action.GetString())
            {
                case "drag":
                    if (!SettingsService.Instance.Current.Overlay.Locked &&
                        !SettingsService.Instance.Current.Overlay.ClickThrough)
                        Dispatcher.Invoke(DragMove);
                    break;
                case "hide":
                case "close":
                    Dispatcher.Invoke(Hide);
                    break;
            }
        }
        catch { }
    }

    public void ApplyConfig(OverlayConfig cfg)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => ApplyConfig(cfg)); return; }
        ApplyClickThrough(new WindowInteropHelper(this).Handle);
    }

    private void ApplyClickThrough(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var cfg = SettingsService.Instance.Current.Overlay;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        ex |= WS_EX_LAYERED | WS_EX_NOACTIVATE;
        if (cfg.ClickThrough) ex |= WS_EX_TRANSPARENT;
        else ex &= ~WS_EX_TRANSPARENT;
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);
    }

    private void SaveBounds()
    {
        if (!IsLoaded) return;
        var s = SettingsService.Instance;
        s.PatchOverlayBounds(new Bounds { X = (int)Left, Y = (int)Top, W = (int)Width, H = (int)Height });
    }
}

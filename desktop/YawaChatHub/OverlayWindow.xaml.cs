using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using YawaChatHub.Services;

namespace YawaChatHub;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public static OverlayWindow? Instance { get; private set; }

    private readonly BridgeHost _bridge;
    private bool _init;
    private bool _boundsReady;

    public OverlayWindow()
    {
        Instance = this;
        _bridge = new BridgeHost { mode = "overlay" };
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyConfig(SettingsService.Instance.Current.Overlay);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var bounds = SettingsService.Instance.Current.OverlayBounds;
        if (bounds != null)
        {
            Left = bounds.X;
            Top = bounds.Y;
            Width = Math.Max(MinWidth, bounds.W);
            Height = Math.Max(MinHeight, bounds.H);
        }
        else
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 24;
            Top = wa.Top + 24;
        }

        LocationChanged += (_, _) => SaveBounds();
        SizeChanged += (_, _) => SaveBounds();
        _boundsReady = true;
    }

    public async void LoadPage()
    {
        if (_init) return;
        _init = true;
        try
        {
            WebAssets.EnsureExtracted();
            await WebView.EnsureCoreWebView2Async();
            var core = WebView.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(
                "yawachat.invalid", WebAssets.AppFolder,
                CoreWebView2HostResourceAccessKind.Allow);
            core.AddHostObjectToScript("sp", _bridge);
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.__YAWA_DESKTOP__=true;try{window.sp=chrome.webview.hostObjects.sync.sp;}catch(e){}");
            core.WebMessageReceived += OnWebMessageReceived;
            _bridge.Script = js => WebView.Dispatcher.Invoke(() =>
            {
                _ = core.ExecuteScriptAsync(js);
            });
            core.Navigate("https://yawachat.invalid/index.html#/overlay");
        }
        catch (Exception ex)
        {
            _init = false;
            System.Windows.MessageBox.Show(
                WebAssets.DescribeFailure(ex), "YawaChatHub — оверлей",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Hide();
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "window") return;
            if (!root.TryGetProperty("action", out var action)) return;

            Dispatcher.Invoke(() =>
            {
                switch (action.GetString())
                {
                    case "drag": BeginDrag(); break;
                    case "hide": Hide(); break;
                    case "close": Hide(); break;
                }
            });
        }
        catch { }
    }

    private void BeginDrag()
    {
        var cfg = SettingsService.Instance.Current.Overlay;
        if (cfg.Locked || cfg.ClickThrough) return;
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            ReleaseCapture();
            SendMessage(hwnd, WmNcLButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
        }
        catch { }
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }
        LoadPage();
        Show();
        Activate();
        ApplyConfig(SettingsService.Instance.Current.Overlay);
    }

    public void ApplyConfig(OverlayConfig cfg)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ApplyConfig(cfg));
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var ex = GetWindowLong(hwnd, GwlExStyle) | WsExLayered;
        if (cfg.ClickThrough)
            ex |= WsExTransparent | WsExNoActivate;
        else
            ex &= ~(WsExTransparent | WsExNoActivate);
        SetWindowLong(hwnd, GwlExStyle, ex);
    }

    private void SaveBounds()
    {
        if (!_boundsReady || !IsLoaded) return;
        SettingsService.Instance.PatchOverlayBounds(new Bounds
        {
            X = (int)Left,
            Y = (int)Top,
            W = (int)Width,
            H = (int)Height,
        });
    }
}

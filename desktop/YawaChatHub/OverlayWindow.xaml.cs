using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
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
        _bridge = new BridgeHost { Mode = "overlay" };
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
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
    }

    public async void LoadPage()
    {
        if (_init) return;
        _init = true;
        await WebView.EnsureCoreWebView2Async();
        var dist = Path.Combine(AppContext.BaseDirectory, "dist");
        if (!Directory.Exists(dist)) dist = Path.Combine(AppContext.BaseDirectory, "..", "dist");
        WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app", dist, CoreWebView2HostResourceAccessKind.Allow);
        WebView.CoreWebView2.AddHostObjectToScript("sp", _bridge);
        WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        WebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
        _bridge.Script = js => WebView.Dispatcher.Invoke(() =>
        {
            WebView.CoreWebView2.ExecuteScriptAsync(js);
        });
        WebView.Source = new Uri("app://index.html#/overlay");
    }

    public void Toggle()
    {
        if (IsVisible) { Hide(); }
        else { LoadPage(); Show(); Activate(); }
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

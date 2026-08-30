using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using YawaChatHub.Services;

namespace YawaChatHub;

public partial class MainWindow : Window
{
    public static MainWindow Instance { get; private set; } = null!;

    private readonly BridgeHost _bridge = new();

    public MainWindow()
    {
        Instance = this;
        BridgeHost.Current = _bridge;
        InitializeComponent();
        Loaded += OnLoaded;
        StateChanged += (_, _) => _bridge.EmitMaximize(WindowState == WindowState.Maximized);
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YawaChatHub", "WebView2");

        var env = await CoreWebView2Environment.CreateAsync(null, userData);
        await WebView.EnsureCoreWebView2Async(env);

        var dist = Path.Combine(AppContext.BaseDirectory, "dist");
        if (!Directory.Exists(dist))
            dist = Path.Combine(AppContext.BaseDirectory, "..", "dist");

        // app://index.html → dist/ (ТЗ §5.1)
        WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app", dist, CoreWebView2HostResourceAccessKind.Allow);

        WebView.CoreWebView2.AddHostObjectToScript("sp", _bridge);
        WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        WebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

        WebView.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
        WebView.Source = new Uri("app://index.html#/app");

        // Перемещение окна мышью за участок с -webkit-app-region: drag
        _bridge.DragRequested += DragMove;
        // Обратный канал событий в JS
        _bridge.Script = js => WebView.Dispatcher.Invoke(() =>
        {
            WebView.CoreWebView2.ExecuteScriptAsync(js);
        });
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var s = SettingsService.Instance.Current;
        if (s.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
        }
        else
        {
            System.Windows.Application.Current.Shutdown();
        }
    }

    public void ToMinimize() => WindowState = WindowState.Minimized;

    public void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    public void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    public void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        Activate();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
    }
}

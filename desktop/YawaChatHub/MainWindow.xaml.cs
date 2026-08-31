using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using YawaChatHub.Services;

namespace YawaChatHub;

public partial class MainWindow : Window
{
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public static MainWindow Instance { get; private set; } = null!;

    private readonly BridgeHost _bridge = new();
    private bool _initializing;
    private bool _coreConfigured;
    private bool _forceExit;

    public MainWindow()
    {
        Instance = this;
        BridgeHost.Current = _bridge;
        InitializeComponent();
        Loaded += async (_, _) => await InitializeWebViewAsync();
        StateChanged += (_, _) => _bridge.EmitMaximize(WindowState == WindowState.Maximized);
        Closing += OnClosing;
    }

    private async Task InitializeWebViewAsync()
    {
        if (_initializing) return;
        _initializing = true;
        ShowLoading("Запуск интерфейса…", "Извлечение встроенного интерфейса из portable EXE");

        try
        {
            WebAssets.EnsureExtracted();
            Log($"Интерфейс извлечён: {WebAssets.AppIndexPath} ({new FileInfo(WebAssets.AppIndexPath).Length} bytes)");

            ShowLoading("Запуск WebView2…", "Подготовка Microsoft Edge WebView2 Runtime");
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YawaChatHub", "WebView2");
            Directory.CreateDirectory(userData);

            if (WebView.CoreWebView2 == null)
            {
                var envTask = CoreWebView2Environment.CreateAsync(null, userData);
                var env = await AwaitWithTimeout(envTask, TimeSpan.FromSeconds(25),
                    "Microsoft Edge WebView2 Runtime не ответил за 25 секунд.");

                var initTask = WebView.EnsureCoreWebView2Async(env);
                await AwaitWithTimeout(initTask, TimeSpan.FromSeconds(25),
                    "Инициализация окна WebView2 не завершилась за 25 секунд.");
            }

            await ConfigureCoreOnceAsync();
            ShowLoading("Загрузка YawaChatHub…", "Открытие встроенной страницы приложения");

            // SetVirtualHostNameToFolderMapping поддерживает HTTP/HTTPS URL.
            // app:// здесь неверен; используем зарезервированный домен .invalid.
            WebView.CoreWebView2.Navigate("https://yawachat.invalid/index.html#/app");
        }
        catch (Exception ex)
        {
            Log("Ошибка инициализации: " + ex);
            ShowFailure(ex);
        }
        finally
        {
            _initializing = false;
        }
    }

    private async Task ConfigureCoreOnceAsync()
    {
        if (_coreConfigured) return;
        var core = WebView.CoreWebView2 ?? throw new InvalidOperationException("CoreWebView2 не инициализирован.");

        core.SetVirtualHostNameToFolderMapping(
            "yawachat.invalid", WebAssets.AppFolder,
            CoreWebView2HostResourceAccessKind.Allow);

        core.AddHostObjectToScript("sp", _bridge);
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;

        // Спецификация WebView2 публикует host object в chrome.webview.hostObjects.
        // Для контракта ТЗ дополнительно создаём window.sp.
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            "window.__YAWA_DESKTOP__=true;" +
            "try{window.sp=chrome.webview.hostObjects.sync.sp;}catch(e){console.error(e);}");

        core.NavigationCompleted += OnNavigationCompleted;
        core.WebMessageReceived += OnWebMessageReceived;
        core.ProcessFailed += (_, e) => Dispatcher.Invoke(() =>
        {
            var kind = e?.ProcessFailedKind.ToString() ?? "Unknown";
            var ex = new InvalidOperationException($"Процесс WebView2 завершился: {kind}");
            Log(ex.Message);
            ShowFailure(ex);
        });
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            try { Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true }); }
            catch { }
        };

        _bridge.DragRequested += BeginNativeDrag;
        _bridge.Script = js => WebView.Dispatcher.Invoke(() =>
        {
            if (WebView.CoreWebView2 != null)
                _ = WebView.CoreWebView2.ExecuteScriptAsync(js);
        });

        _coreConfigured = true;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            Log("Навигация завершена успешно: " + WebView.Source);
            WebView.Visibility = Visibility.Visible;
            StatusPanel.Visibility = Visibility.Collapsed;
            WebView.Focus();
            return;
        }

        var ex = new InvalidOperationException(
            $"WebView2 не открыл встроенную страницу: {e.WebErrorStatus} ({WebView.Source})");
        Log(ex.Message);
        ShowFailure(ex);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "window") return;
            if (!root.TryGetProperty("action", out var actionElement)) return;

            switch (actionElement.GetString())
            {
                case "minimize": ToMinimize(); break;
                case "maximize": ToggleMaximize(); break;
                case "close": Close(); break;
                case "hide": HideToTray(); break;
                case "quit": Quit(); break;
                case "drag": BeginNativeDrag(); break;
            }
        }
        catch (Exception ex)
        {
            Log("Некорректное сообщение WebView: " + ex.Message);
        }
    }

    private static async Task<T> AwaitWithTimeout<T>(Task<T> task, TimeSpan timeout, string message)
    {
        if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
            throw new TimeoutException(message);
        return await task;
    }

    private static async Task AwaitWithTimeout(Task task, TimeSpan timeout, string message)
    {
        if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
            throw new TimeoutException(message);
        await task;
    }

    private void ShowLoading(string title, string text)
    {
        StatusPanel.Visibility = Visibility.Visible;
        StatusTitle.Text = title;
        StatusText.Text = text;
        ErrorActions.Visibility = Visibility.Collapsed;
    }

    private void ShowFailure(Exception ex)
    {
        StatusPanel.Visibility = Visibility.Visible;
        StatusTitle.Text = "Не удалось открыть интерфейс";
        StatusText.Text = WebAssets.DescribeFailure(ex);
        ErrorActions.Visibility = Visibility.Visible;
    }

    private void StatusRetry_Click(object sender, RoutedEventArgs e) =>
        _ = InitializeWebViewAsync();

    private void StatusClose_Click(object sender, RoutedEventArgs e) => Quit();

    private void StatusTitle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ToggleMaximize();
        else BeginNativeDrag();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F4 &&
            System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt))
            Quit();
    }

    private void BeginNativeDrag()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            ReleaseCapture();
            SendMessage(hwnd, WmNcLButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
        }
        catch { }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_forceExit && SettingsService.Instance.Current.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        try { WidgetServer.Instance.Stop(); } catch { }
        try { TrayManager.Instance.Dispose(); } catch { }
    }

    public void Quit()
    {
        _forceExit = true;
        System.Windows.Application.Current.Shutdown();
    }

    public void ToMinimize()
    {
        if (SettingsService.Instance.Current.MinimizeToTray) HideToTray();
        else WindowState = WindowState.Minimized;
    }

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

    private static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YawaChatHub", "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "startup.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}

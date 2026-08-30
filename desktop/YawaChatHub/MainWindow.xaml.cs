using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using YawaChatHub.Services;

namespace YawaChatHub;

public partial class MainWindow : Window
{
    private const string RuntimeUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    public static MainWindow Instance { get; private set; } = null!;

    private readonly BridgeHost _bridge = new();
    private DispatcherTimer? _watchdog;
    private bool _uiReady;
    private bool _initializing;

    public MainWindow()
    {
        Instance = this;
        BridgeHost.Current = _bridge;
        InitializeComponent();
        Loaded += OnLoaded;
        StateChanged += (_, _) => _bridge.EmitMaximize(WindowState == WindowState.Maximized);
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => StartWebView();

    /// <summary>
    /// Инициализация WebView2 с полной защитой от сбоев: любая ошибка теперь
    /// показывает панель диагностики, а не оставляет пустое неубиваемое окно.
    /// </summary>
    private async void StartWebView()
    {
        if (_initializing) return;
        _initializing = true;

        ShowSplash("Подготовка интерфейса…");

        try
        {
            // 1. Распаковываем встроенный HTML (portable exe самодостаточен)
            WebAssets.Ensure();
            if (!WebAssets.AppReady)
            {
                ShowError($"Не удалось подготовить файлы интерфейса.\n{WebAssets.LastError}", runtimeMissing: false);
                return;
            }

            // 2. Проверяем наличие WebView2 Runtime до создания окружения
            try
            {
                var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                if (string.IsNullOrWhiteSpace(version))
                    throw new WebView2RuntimeNotFoundException("WebView2 Runtime не установлен");
            }
            catch (WebView2RuntimeNotFoundException)
            {
                ShowError(
                    "На компьютере не установлен компонент Microsoft Edge WebView2 Runtime — " +
                    "без него приложение не может отрисовать интерфейс.",
                    runtimeMissing: true);
                return;
            }

            ShowSplash("Запуск WebView2…");

            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YawaChatHub", "WebView2");
            Directory.CreateDirectory(userData);

            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await WebView.EnsureCoreWebView2Async(env);

            var core = WebView.CoreWebView2;

            // app://index.html → распакованный каталог с интерфейсом (ТЗ §5.1)
            core.SetVirtualHostNameToFolderMapping(
                "app", WebAssets.Root, CoreWebView2HostResourceAccessKind.Allow);

            core.AddHostObjectToScript("sp", _bridge);
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.NewWindowRequested += (_, args) => args.Handled = true;

            core.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess) ShowInterface();
                else ShowError($"Страница интерфейса не открылась (код {args.WebErrorStatus}).", false);
            };
            core.ProcessFailed += (_, _) =>
                ShowError("Процесс WebView2 аварийно завершился.", false);

            // Обратный канал событий в JS (не блокирует вызывающий поток)
            _bridge.Script = js =>
            {
                if (!_uiReady) return;
                WebView.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { WebView.CoreWebView2?.ExecuteScriptAsync(js); } catch { }
                }));
            };

            // Перетаскивание окна за HTML-шапку (-webkit-app-region: drag)
            _bridge.DragRequested += SafeDragMove;

            ShowSplash("Загрузка интерфейса…");
            StartWatchdog();
            WebView.Source = new Uri("app://index.html#/app");
        }
        catch (Exception ex)
        {
            ShowError($"Ошибка запуска интерфейса:\n{ex.Message}", ex is WebView2RuntimeNotFoundException);
        }
        finally
        {
            _initializing = false;
        }
    }

    /// <summary>Если за 20 секунд интерфейс не появился — показываем диагностику.</summary>
    private void StartWatchdog()
    {
        _watchdog?.Stop();
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _watchdog.Tick += (_, _) =>
        {
            _watchdog?.Stop();
            if (!_uiReady)
                ShowError("Интерфейс не ответил за 20 секунд. Попробуйте повторить запуск.", false);
        };
        _watchdog.Start();
    }

    private void ShowSplash(string text)
    {
        SplashText.Text = text;
        Splash.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;
        WebView.Visibility = Visibility.Collapsed;
    }

    private void ShowInterface()
    {
        _uiReady = true;
        _watchdog?.Stop();
        WebView.Visibility = Visibility.Visible;
        Splash.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message, bool runtimeMissing)
    {
        _uiReady = false;
        _watchdog?.Stop();
        ErrorText.Text = message;
        RuntimeButton.Visibility = runtimeMissing ? Visibility.Visible : Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;
        Splash.Visibility = Visibility.Collapsed;
        WebView.Visibility = Visibility.Collapsed;
    }

    private void SafeDragMove()
    {
        try
        {
            if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove();
        }
        catch { /* кнопка уже отпущена */ }
    }

    private void OnChromeDrag(object sender, MouseButtonEventArgs e)
    {
        try { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
        catch { }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnRetryClick(object sender, RoutedEventArgs e) => StartWebView();

    private void OnInstallRuntimeClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(RuntimeUrl) { UseShellExecute = true }); }
        catch { }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // В трей прячем только когда интерфейс жив — иначе окно нельзя вернуть
        if (_uiReady && SettingsService.Instance.Current.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        System.Windows.Application.Current.Shutdown();
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

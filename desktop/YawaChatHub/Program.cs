using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using YawaChatHub.Services;

namespace YawaChatHub;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Обход корпоративных прокси с TLS-инспекцией (ТЗ §15): приложение
        // только читает публичные чаты. Флаг задаётся до любых сетевых вызовов.
        Environment.SetEnvironmentVariable("NODE_TLS_REJECT_UNAUTHORIZED", "0");
        System.Net.ServicePointManager
            .ServerCertificateValidationCallback = (_, _, _, _) => true;

        var app = new System.Windows.Application
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
        };

        // Ни одна ошибка больше не «подвешивает» приложение молча
        app.DispatcherUnhandledException += OnDispatcherError;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Report((e.ExceptionObject as Exception)?.Message ?? "неизвестная ошибка");
        TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved();

        var settings = SettingsService.Instance;

        // Окно показывается ПЕРВЫМ, службы стартуют параллельно (ТЗ §23 п.10)
        var main = new MainWindow();
        if (!settings.Current.StartHidden) main.Show();

        TrayManager.Instance.Init(app, main);

        main.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            Task.Run(() => StartServices(settings))));

        app.Exit += (_, _) => Cleanup();
        app.Run();

        // Гарантируем выход процесса, даже если какая-то служба ещё живёт
        Cleanup();
        Environment.Exit(0);
    }

    /// <summary>Фоновая инициализация: сервер виджета, хоткеи, коннекторы.</summary>
    private static void StartServices(SettingsService settings)
    {
        try { WidgetServer.Instance.Start(settings.Current.Port); } catch { }

        try
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(
                () => HotkeyManager.Instance.RegisterDefaults());
        }
        catch { }

        try
        {
            foreach (var ch in settings.Current.Channels)
                ConnectorManager.Instance.Add(ch.Platform, ch.ChannelId);
        }
        catch { }
    }

    private static bool _cleaned;

    private static void Cleanup()
    {
        if (_cleaned) return;
        _cleaned = true;
        try { TrayManager.Instance.Dispose(); } catch { }
        try { WidgetServer.Instance.Stop(); } catch { }
        try { HotkeyManager.Instance.Dispose(); } catch { }
        try { TtsService.Instance.StopAll(); } catch { }
    }

    private static void OnDispatcherError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Report(e.Exception.Message);
    }

    private static void Report(string message)
    {
        try
        {
            System.Windows.MessageBox.Show(
                $"YawaChatHub столкнулся с ошибкой:\n\n{message}",
                "YawaChatHub",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        catch { }
    }
}

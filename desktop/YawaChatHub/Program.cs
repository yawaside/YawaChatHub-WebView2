using System;
using System.Threading.Tasks;
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

        // settings.json читается быстро локально; главное окно создаём и показываем
        // ДО запуска сетевых служб, чтобы первый запуск не выглядел зависшим.
        var settings = SettingsService.Instance;
        var main = new MainWindow();
        TrayManager.Instance.Init(app, main);

        if (!settings.Current.StartHidden)
            main.Show();

        // Службы запускаются после старта UI и не блокируют главное окно.
        app.Dispatcher.BeginInvoke(new Action(() =>
        {
            try { HotkeyManager.Instance.RegisterDefaults(); } catch { }

            _ = Task.Run(() =>
            {
                try { WidgetServer.Instance.Start(settings.Current.Port); } catch { }
            });

            foreach (var ch in settings.Current.Channels)
            {
                try { ConnectorManager.Instance.Add(ch.Platform, ch.ChannelId); }
                catch { }
            }
        }), DispatcherPriority.Background);

        app.Run();
    }
}

using System;
using System.Windows;
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

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        // Первый запуск: settings.json + генерация token + WidgetServer (ТЗ §20)
        var settings = SettingsService.Instance;
        WidgetServer.Instance.Start(settings.Current.Port);
        HotkeyManager.Instance.RegisterDefaults();

        var main = new MainWindow();
        if (!settings.Current.StartHidden)
            main.Show();

        // Подключаем каналы из settings.channels (ТЗ §20)
        foreach (var ch in settings.Current.Channels)
            ConnectorManager.Instance.Add(ch.Platform, ch.ChannelId);

        app.Run();
    }
}

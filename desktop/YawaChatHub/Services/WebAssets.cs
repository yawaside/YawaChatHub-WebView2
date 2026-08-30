using System;
using System.IO;
using System.Reflection;

namespace YawaChatHub.Services;

/// <summary>
/// Извлекает встроенный Vite single-file HTML и OBS-виджет из portable EXE.
/// В релизе публикуется один YawaChatHub.exe, поэтому внешней папки dist нет.
/// WebView2 требует реальную папку для SetVirtualHostNameToFolderMapping.
/// </summary>
public static class WebAssets
{
    private const string AppResource = "YawaChatHub.Web.index.html";
    private const string WidgetResource = "YawaChatHub.Web.widget.html";

    private static readonly object Sync = new();
    private static bool _ready;

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YawaChatHub", "Web");

    public static string AppFolder => Path.Combine(Root, "app");
    public static string AppIndexPath => Path.Combine(AppFolder, "index.html");
    public static string WidgetFolder => Path.Combine(Root, "widget");
    public static string WidgetHtmlPath => Path.Combine(WidgetFolder, "index.html");

    public static void EnsureExtracted()
    {
        lock (Sync)
        {
            if (_ready && File.Exists(AppIndexPath) && new FileInfo(AppIndexPath).Length > 100)
                return;

            Directory.CreateDirectory(AppFolder);
            Directory.CreateDirectory(WidgetFolder);

            Extract(AppResource, AppIndexPath);
            Extract(WidgetResource, WidgetHtmlPath);

            if (!File.Exists(AppIndexPath) || new FileInfo(AppIndexPath).Length < 100)
                throw new InvalidOperationException("Встроенный интерфейс приложения повреждён или отсутствует.");

            _ready = true;
        }
    }

    private static void Extract(string resourceName, string destination)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var source = asm.GetManifestResourceStream(resourceName);
        if (source == null)
        {
            var available = string.Join(", ", asm.GetManifestResourceNames());
            throw new FileNotFoundException(
                $"Встроенный ресурс '{resourceName}' не найден. Доступны: {available}");
        }

        // Перезаписываем на каждом запуске: после обновления EXE пользователь
        // гарантированно получает интерфейс той же версии, а не старый кэш.
        var temp = destination + ".tmp";
        using (var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            source.CopyTo(target);

        File.Move(temp, destination, true);
    }

    public static string DescribeFailure(Exception ex)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "неизвестна";
        return
            $"Не удалось запустить интерфейс YawaChatHub.\n\n" +
            $"Версия: {version}\n" +
            $"Каталог интерфейса: {AppFolder}\n" +
            $"Ошибка: {ex.GetType().Name}: {ex.Message}\n\n" +
            "Проверьте наличие Microsoft Edge WebView2 Runtime и права записи в LocalAppData.";
    }
}

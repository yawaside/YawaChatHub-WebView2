using System;
using System.IO;
using System.Reflection;

namespace YawaChatHub.Services;

/// <summary>
/// Встроенный фронтенд (Vite single-file dist/index.html) извлекается на диск,
/// чтобы WebView2 отдал его через SetVirtualHostNameToFolderMapping.
///
/// Зачем: portable-сборка — это ОДИН файл YawaChatHub.exe. Раньше окно грузило
/// dist из папки рядом с exe, которой в релизе нет, из-за чего WebView2
/// показывал пустую страницу. Теперь HTML зашит в сам exe.
/// </summary>
public static class WebAssets
{
    private const string AppHtmlResource = "YawaChatHub.dist.index.html";
    private const string WidgetHtmlResource = "YawaChatHub.widget.index.html";

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YawaChatHub", "web");

    public static string AppHtmlPath => Path.Combine(Root, "index.html");
    public static string WidgetHtmlPath => Path.Combine(Root, "widget.html");

    /// <summary>true, если интерфейс успешно распакован и готов к показу.</summary>
    public static bool AppReady { get; private set; }

    /// <summary>Текст последней ошибки распаковки (для окна диагностики).</summary>
    public static string? LastError { get; private set; }

    public static void Ensure()
    {
        try
        {
            Directory.CreateDirectory(Root);

            AppReady = TryWrite(AppHtmlResource, AppHtmlPath);
            TryWrite(WidgetHtmlResource, WidgetHtmlPath);

            // Фолбэк для dev-запуска: dist рядом с exe или на два уровня выше
            if (!AppReady)
            {
                foreach (var candidate in new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "dist", "index.html"),
                    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "dist", "index.html"),
                })
                {
                    if (!File.Exists(candidate)) continue;
                    File.Copy(candidate, AppHtmlPath, true);
                    AppReady = true;
                    break;
                }
            }

            if (!AppReady)
                LastError = "Ресурс интерфейса не найден в сборке (dist/index.html не был собран Vite).";
        }
        catch (Exception ex)
        {
            AppReady = false;
            LastError = ex.Message;
        }
    }

    private static bool TryWrite(string resource, string target)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            if (stream == null) return false;

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();
            if (bytes.Length == 0) return false;

            // перезаписываем только при изменении — ускоряет повторные запуски
            if (File.Exists(target) && new FileInfo(target).Length == bytes.Length)
                return true;

            File.WriteAllBytes(target, bytes);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }
}

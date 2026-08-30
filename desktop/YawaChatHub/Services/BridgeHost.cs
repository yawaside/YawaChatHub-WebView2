using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;

namespace YawaChatHub.Services;

/// <summary>
/// COM-объект `sp` (SpBridge), публикуемый в WebView2 через AddHostObjectToScript.
/// Ответы методов чтения — всегда JSON-строка (JS парсит через JSON.parse).
/// События в JS уходят через window.dispatchEvent(new CustomEvent(...)).
/// </summary>
[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class BridgeHost
{
    public BridgeHost()
    {
        // UI немедленно получает сохранённые C#-настройки после любого patch.
        // Обновления виджета/оверлея тоже идут из одного источника состояния.
        SettingsService.Instance.Changed += OnSettingsChanged;
    }

    public string mode { get; set; } = "app";
    public string platform { get; } = "win32";

    /// <summary>Текущий мост (устанавливается окном), доступен сервисам для эмиссии событий.</summary>
    public static BridgeHost? Current { get; set; }

    /// <summary>Обратный канал в JS (настраивается окном после инициализации WebView2).</summary>
    public Action<string>? Script { get; set; }

    public event Action? DragRequested;

    private static string Json(object o) => JsonSerializer.Serialize(o);

    private void Emit(string eventName, string detail)
    {
        var js = $"window.dispatchEvent(new CustomEvent('{eventName}', {{ detail: {detail} }}));";
        Script?.Invoke(js);
    }

    private void OnSettingsChanged(Settings settings)
    {
        // Настройки синхронизирует только bridge главного окна. Оверлей читает
        // их при создании; это исключает двойные вызовы Apply/HotkeyManager.
        if (Current != this) return;
        EmitSettings(settings);
        try { WidgetServer.Instance.SendConfig(SettingsService.Instance.ToJson()); } catch { }
        try { OverlayManager.Apply(settings.Overlay); } catch { }
        try { HotkeyManager.Instance.Apply(settings.Hotkeys); } catch { }
    }

    // ── Каналы ──────────────────────────────────────────────────────────────
    public string getChannels() => Json(ConnectorManager.Instance.List());

    public void addChannel(string platform, string channelId) =>
        ConnectorManager.Instance.Add(platform, channelId);

    public void removeChannel(string platform, string channelId) =>
        ConnectorManager.Instance.Remove(platform, channelId);

    public void diagnoseNet() => ConnectorManager.Instance.Diagnose();

    // ── Виджет OBS ───────────────────────────────────────────────────────────
    public string widgetUrl() => WidgetServer.Instance.Url;

    public string widgetInfo() => Json(WidgetServer.Instance.Info);

    public void widgetTest(string msgJson)
    {
        try { WidgetServer.Instance.Broadcast(new { type = "chat", msg = JsonSerializer.Deserialize<object>(msgJson) }); } catch { }
    }

    public void widgetConfig(string cfgJson) => WidgetServer.Instance.SendConfig(cfgJson);

    // ── Настройки ────────────────────────────────────────────────────────────
    public string settingsGet() => SettingsService.Instance.ToJson();

    public void settingsPatch(string patchJson) => SettingsService.Instance.Patch(patchJson);

    // ── Окно (в главном потоке WPF Dispatcher) ───────────────────────────────
    private static void UI(Action a)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) a();
        else d.Invoke(a);
    }

    public void minimize() => UI(() => MainWindow.Instance.ToMinimize());
    public void toggleMaximize() => UI(() => MainWindow.Instance.ToggleMaximize());
    public void hideToTray() => UI(() => MainWindow.Instance.HideToTray());
    public void close() => UI(() => MainWindow.Instance.Close());
    public bool isMaximized() => MainWindow.Instance.WindowState == WindowState.Maximized;
    public void dragMove() => DragRequested?.Invoke();

    // ── Озвучка (SAPI5) ──────────────────────────────────────────────────────
    public void ttsSpeak(string json) => TtsService.Instance.Enqueue(json);
    public void ttsSkip() => TtsService.Instance.Skip();
    public void ttsStopAll() => TtsService.Instance.StopAll();
    public string ttsVoices() => Json(TtsService.Instance.Voices());

    // ── Оверлей ──────────────────────────────────────────────────────────────
    public string overlayGet() => Json(SettingsService.Instance.Current.Overlay);
    public void overlaySet(string cfgJson)
    {
        try
        {
            var cfg = JsonSerializer.Deserialize<OverlayConfig>(cfgJson);
            if (cfg != null)
            {
                SettingsService.Instance.Patch($"{{\"overlay\": {cfgJson}}}");
                OverlayManager.Apply(cfg);
            }
        }
        catch { }
    }

    // ── Приложение ───────────────────────────────────────────────────────────
    public void appQuit() => UI(() => MainWindow.Instance.Quit());

    // ── Внутренние эмиттеры (вызываются сервисами) ───────────────────────────
    public void EmitChat(ChatMsg msg)
    {
        // Один полученный connector-ом ChatMsg расходится одновременно в
        // основную ленту и OBS WidgetServer. Никакой генерации/подмены данных.
        try { WidgetServer.Instance.Broadcast(new { type = "chat", msg }); } catch { }
        Emit("sp:chat", Json(msg));
    }
    public void EmitChannels(List<Channel> channels) => Emit("sp:channels", Json(channels));
    public void EmitSettings(Settings s) => Emit("sp:settings", SettingsService.Instance.ToJson());
    public void EmitHotkey(string action) => Emit("sp:hotkey", Json(action));
    public void EmitTtsEnd(string id) => Emit("sp:tts:end", Json(id));
    public void EmitMaximize(bool v) => Emit("sp:maximize", v ? "true" : "false");
    public void EmitWidgetClients(int n) => Emit("sp:widget:clients", n.ToString());
}

/// <summary>Управление окном оверлея (обёртка для доступа из сервисов).</summary>
public static class OverlayManager
{
    private static OverlayWindow? _win;

    public static void SetWindow(OverlayWindow w) => _win = w;

    public static void Toggle()
    {
        if (_win == null)
        {
            _win = new OverlayWindow();
        }
        _win.Toggle();
    }

    public static void Apply(OverlayConfig cfg)
    {
        if (_win == null && cfg.Enabled)
        {
            _win = new OverlayWindow();
        }
        _win?.ApplyConfig(cfg);
        if (_win != null && !cfg.Enabled) _win.Hide();
        if (_win != null && cfg.Enabled) { _win.LoadPage(); _win.Show(); }
    }
}

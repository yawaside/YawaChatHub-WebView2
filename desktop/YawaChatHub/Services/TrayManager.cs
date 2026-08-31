using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace YawaChatHub.Services;

/// <summary>Иконка в трее + меню (ТЗ §5.3).</summary>
public sealed class TrayManager : IDisposable
{
    private static TrayManager? _instance;
    public static TrayManager Instance => _instance ??= new TrayManager();

    private NotifyIcon? _icon;
    private MainWindow? _main;

    private TrayManager() { }

    public void Init(System.Windows.Application app, MainWindow main)
    {
        _main = main;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть YawaChatHub", null, (_, _) => main.ShowFromTray());
        menu.Items.Add("Свернуть окно в трей", null, (_, _) => main.HideToTray());
        menu.Items.Add(new ToolStripSeparator());

        var closeToTray = new ToolStripMenuItem("Сворачивать в трей при закрытии")
        { Checked = SettingsService.Instance.Current.CloseToTray };
        closeToTray.Click += (_, _) =>
        {
            var s = SettingsService.Instance;
            s.Patch($"{{\"closeToTray\": {(!s.Current.CloseToTray).ToString().ToLowerInvariant()}}}");
            closeToTray.Checked = s.Current.CloseToTray;
        };
        menu.Items.Add(closeToTray);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Озвучка вкл/выкл", null, (_, _) => BridgeHost.Current?.EmitHotkey("tts:toggle"));
        menu.Items.Add("Пропустить текущее", null, (_, _) => TtsService.Instance.Skip());
        menu.Items.Add("Очистить очередь", null, (_, _) => TtsService.Instance.StopAll());
        menu.Items.Add(new ToolStripSeparator());

        var overlayItem = new ToolStripMenuItem("Игровой оверлей вкл/выкл")
        { Checked = SettingsService.Instance.Current.Overlay.Enabled };
        overlayItem.Click += (_, _) =>
        {
            OverlayManager.Toggle();
            overlayItem.Checked = SettingsService.Instance.Current.Overlay.Enabled;
        };
        menu.Items.Add(overlayItem);

        var clickThroughItem = new ToolStripMenuItem("Сквозные клики вкл/выкл")
        { Checked = SettingsService.Instance.Current.Overlay.ClickThrough };
        clickThroughItem.Click += (_, _) =>
        {
            var s = SettingsService.Instance;
            s.Patch($"{{\"overlay\": {{\"clickThrough\": {(!s.Current.Overlay.ClickThrough).ToString().ToLowerInvariant()}}}}}");
            clickThroughItem.Checked = s.Current.Overlay.ClickThrough;
        };
        menu.Items.Add(clickThroughItem);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Выход", null, (_, _) => app.Shutdown());

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "YawaChatHub",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => main.ShowFromTray();
    }

    private static Icon LoadIcon()
    {
        try
        {
            var p = System.IO.Path.Combine(AppContext.BaseDirectory, "..", "build", "icon.ico");
            if (System.IO.File.Exists(p)) return new Icon(p);
        }
        catch { }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}

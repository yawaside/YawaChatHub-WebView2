using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace YawaChatHub.Services;

/// <summary>
/// Глобальные горячие клавиши через WinAPI RegisterHotKey (ТЗ §17).
/// Работают даже со свёрнутым окном. overlay:toggle / overlay:clicks /
/// window:toggle обрабатываются локально в C#, остальные — в JS.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private static readonly Lazy<HotkeyManager> _lazy = new(() => new HotkeyManager());
    public static HotkeyManager Instance => _lazy.Value;

    private HwndSource? _source;
    private readonly Dictionary<int, string> _actions = new();
    private int _nextId = 1;

    private HotkeyManager() { }

    public void RegisterDefaults()
    {
        var map = SettingsService.Instance.Current.Hotkeys;
        if (map == null || map.Count == 0)
        {
            map = new Dictionary<string, string>
            {
                ["overlay:toggle"] = "Control+Shift+G",
                ["overlay:clicks"] = "Control+Shift+C",
                ["tts:toggle"] = "Control+Shift+T",
                ["tts:pause"] = "Control+Shift+P",
                ["tts:skip"] = "Control+Shift+S",
                ["tts:clear"] = "Control+Shift+Q",
                ["window:toggle"] = "Control+Shift+H",
                ["feed:clear"] = "Control+Shift+L",
            };
        }
        Apply(map);
    }

    public void Apply(Dictionary<string, string> map)
    {
        _source ??= CreateMessageWindow();
        UnregisterAll();

        foreach (var kv in map)
        {
            if (!TryParse(kv.Value, out var mod, out var vk)) continue;
            var id = _nextId++;
            if (RegisterHotKey(_source!.Handle, id, mod, vk))
                _actions[id] = kv.Key;
        }
    }

    private HwndSource CreateMessageWindow()
    {
        // HWND_MESSAGE (-3): message-only окно — невидимо и не мелькает на экране
        var param = new HwndSourceParameters("YawaChatHubHotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            ParentWindow = new IntPtr(-3),
        };
        var src = new HwndSource(param);
        src.AddHook(WndProc);
        return src;
    }

    public void Dispose()
    {
        try { UnregisterAll(); } catch { }
        try { _source?.Dispose(); } catch { }
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            Dispatch(action);
        }
        return IntPtr.Zero;
    }

    private void Dispatch(string action)
    {
        switch (action)
        {
            case "overlay:toggle":
                OverlayManager.Toggle();
                break;
            case "overlay:clicks":
                var s = SettingsService.Instance;
                s.Patch($"{{\"overlay\": {{\"clickThrough\": {(!s.Current.Overlay.ClickThrough).ToString().ToLowerInvariant()}}}}}");
                OverlayManager.Apply(s.Current.Overlay);
                break;
            case "window:toggle":
                if (MainWindow.Instance.IsVisible)
                    MainWindow.Instance.HideToTray();
                else
                    MainWindow.Instance.ShowFromTray();
                break;
            default:
                BridgeHost.Current?.EmitHotkey(action);
                break;
        }
    }

    private void UnregisterAll()
    {
        if (_source == null) return;
        foreach (var id in _actions.Keys) UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    private static bool TryParse(string combo, out uint mod, out uint vk)
    {
        mod = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(combo)) return false;
        var parts = combo.Split('+');
        var key = parts[^1].Trim();
        foreach (var p in parts[..^1])
        {
            var t = p.Trim().ToLowerInvariant();
            if (t == "control" || t == "ctrl") mod |= MOD_CONTROL;
            else if (t == "shift") mod |= MOD_SHIFT;
            else if (t == "alt") mod |= MOD_ALT;
        }
        var v = VkFromKey(key);
        if (v == 0) return false;
        vk = (uint)v;
        return true;
    }

    private static int VkFromKey(string key)
    {
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c >= 'A' && c <= 'Z') return c;
            if (c >= '0' && c <= '9') return c;
        }
        return key.ToUpperInvariant() switch
        {
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73, "F5" => 0x74,
            "F6" => 0x75, "F7" => 0x76, "F8" => 0x77, "F9" => 0x78, "F10" => 0x79,
            "F11" => 0x7A, "F12" => 0x7B,
            "SPACE" => 0x20, "HOME" => 0x24, "END" => 0x23, "INSERT" => 0x2D,
            "DELETE" => 0x2E, "PAGEUP" => 0x21, "PAGEDOWN" => 0x22,
            _ => 0,
        };
    }
}

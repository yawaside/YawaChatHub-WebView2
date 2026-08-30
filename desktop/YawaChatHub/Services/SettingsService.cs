using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YawaChatHub.Services;

// ─── Модели настроек (settings.json, ТЗ §11) ────────────────────────────────

public class Channel
{
    public string Platform { get; set; } = "twitch";
    public string ChannelId { get; set; } = "";
}

public class OverlayConfig
{
    public bool Enabled { get; set; }
    public int BgOpacity { get; set; } = 55;
    public bool ClickThrough { get; set; }
    public string Mode { get; set; } = "compact";
    public int FontSize { get; set; } = 12;
    public int MaxMessages { get; set; } = 6;
    public bool Locked { get; set; }
    public string Style { get; set; } = "clean";
    public bool ShowBorder { get; set; } = true;
    public double EffectDuration { get; set; } = 0.3;
    public string TextColor { get; set; } = "";
    public string NameColor { get; set; } = "";
    public string BgColor { get; set; } = "";
    public int Radius { get; set; } = 14;
    public string BgImage { get; set; } = "";
    public bool ShowTime { get; set; }
    public bool ShowPlatform { get; set; } = true;
}

public class TtsConfig
{
    public bool Enabled { get; set; }
    public double Rate { get; set; } = 1.0;
    public double Volume { get; set; } = 0.9;
    public string VoiceURI { get; set; } = "";
    public bool ObsTts { get; set; }
    public Dictionary<string, bool> Template { get; set; } = new()
    {
        ["author"] = true, ["platform"] = true, ["text"] = true,
    };
}

public class Bounds
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
}

public class Settings
{
    public int SettingsSchemaVersion { get; set; } = 4;
    public int Port { get; set; } = 47823;
    public string Token { get; set; } = "";
    public string Theme { get; set; } = "midnight";
    public bool CloseToTray { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool StartHidden { get; set; }
    public string YoutubeApiKey { get; set; } = "";
    public Bounds? OverlayBounds { get; set; }
    public bool ChannelsCollapsed { get; set; }
    public bool MenuCollapsed { get; set; }
    public bool ShowEvents { get; set; } = true;
    public List<Channel> Channels { get; set; } = new();
    public TtsConfig Tts { get; set; } = new();
    public Dictionary<string, object> ChatView { get; set; } = new();
    public Dictionary<string, object> Widget { get; set; } = new();
    public OverlayConfig Overlay { get; set; } = new();
    public Dictionary<string, string> Hotkeys { get; set; } = new();
}

// ─── Сервис настроек (автосохранение без кнопок «Сохранить») ───────────────

public sealed class SettingsService
{
    private static readonly Lazy<SettingsService> _lazy = new(() => new SettingsService());
    public static SettingsService Instance => _lazy.Value;

    private readonly string _path;
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public Settings Current { get; private set; } = new();

    public event Action<Settings>? Changed;

    private SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YawaChatHub");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");

        if (File.Exists(_path))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path));
                if (saved != null) Current = Migrate(saved);
            }
            catch { /* повреждённый файл — дефолты */ }
        }

        if (string.IsNullOrEmpty(Current.Token) || Current.Token == "yawa_demo")
            Current.Token = "yawa_" + Guid.NewGuid().ToString("N")[..12];
        Save();
    }

    private static Settings Migrate(Settings s)
    {
        if (s.SettingsSchemaVersion < 4)
        {
            s.Overlay ??= new OverlayConfig();
            s.ChatView ??= new Dictionary<string, object>();
            s.Widget ??= new Dictionary<string, object>();
            s.Hotkeys ??= new Dictionary<string, string>();
        }
        if (s.SettingsSchemaVersion < 3)
            s.CloseToTray = false; // ТЗ §11: при схеме < 3 closeToTray обязательно false
        s.SettingsSchemaVersion = 4;
        return s;
    }

    public void Patch(string json)
    {
        try
        {
            var patch = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (patch == null) return;
            lock (_lock)
            {
                var merged = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(Current))!;
                ApplyPatch(merged, patch);
                Current = merged;
                Save();
            }
            Changed?.Invoke(Current);
        }
        catch { /* некорректный patch игнорируется */ }
    }

    private static void ApplyPatch(Settings target, Dictionary<string, JsonElement> patch)
    {
        foreach (var kv in patch)
        {
            try
            {
                if (kv.Key == "tts")
                    target.Tts = JsonSerializer.Deserialize<TtsConfig>(kv.Value.GetRawText())!;
                else if (kv.Key == "overlay")
                    target.Overlay = JsonSerializer.Deserialize<OverlayConfig>(kv.Value.GetRawText())!;
                else if (kv.Key == "channels")
                    target.Channels = JsonSerializer.Deserialize<List<Channel>>(kv.Value.GetRawText())!;
                else if (kv.Key == "theme") target.Theme = kv.Value.GetString() ?? target.Theme;
                else if (kv.Key == "closeToTray") target.CloseToTray = kv.Value.GetBoolean();
                else if (kv.Key == "minimizeToTray") target.MinimizeToTray = kv.Value.GetBoolean();
                else if (kv.Key == "startHidden") target.StartHidden = kv.Value.GetBoolean();
                else if (kv.Key == "showEvents") target.ShowEvents = kv.Value.GetBoolean();
                else if (kv.Key == "menuCollapsed") target.MenuCollapsed = kv.Value.GetBoolean();
                else if (kv.Key == "channelsCollapsed") target.ChannelsCollapsed = kv.Value.GetBoolean();
                else if (kv.Key == "hotkeys")
                    target.Hotkeys = JsonSerializer.Deserialize<Dictionary<string, string>>(kv.Value.GetRawText())!;
                else if (kv.Key == "chatView")
                    target.ChatView = JsonSerializer.Deserialize<Dictionary<string, object>>(kv.Value.GetRawText())!;
                else if (kv.Key == "widget")
                    target.Widget = JsonSerializer.Deserialize<Dictionary<string, object>>(kv.Value.GetRawText())!;
            }
            catch { /* поле с ошибкой пропускаем */ }
        }
    }

    public void PatchOverlayBounds(Bounds b)
    {
        lock (_lock)
        {
            Current.OverlayBounds = b;
            Save();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(Current);

    private void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(Current, _json)); }
        catch { /* диск недоступен */ }
    }
}

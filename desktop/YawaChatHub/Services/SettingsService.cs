using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace YawaChatHub.Services;

// ─── Модели settings.json (ТЗ §11) ─────────────────────────────────────────

public class Channel
{
    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "twitch";

    [JsonPropertyName("channelId")]
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
    public string Effect { get; set; } = "fx-slide-up";
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
    public Dictionary<string, object> Filters { get; set; } = new();
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
    // В desktop дефолт намеренно пустой: приложение не генерирует демонстрационные
    // каналы и не читает ничего, пока пользователь не добавит реальный канал.
    public List<Channel> Channels { get; set; } = new();
    public TtsConfig Tts { get; set; } = new();
    public Dictionary<string, object> ChatView { get; set; } = new();
    public Dictionary<string, object> Widget { get; set; } = new();
    public OverlayConfig Overlay { get; set; } = new();
    public Dictionary<string, string> Hotkeys { get; set; } = new()
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

/// <summary>
/// Локальный settings.json. Любая настройка сохраняется сразу; patch — глубокий,
/// поэтому `{ tts: { enabled: true } }` не стирает скорость/фильтры/голос.
/// </summary>
public sealed class SettingsService
{
    private static readonly Lazy<SettingsService> _lazy = new(() => new SettingsService());
    public static SettingsService Instance => _lazy.Value;

    private readonly string _path;
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

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
                var saved = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path), _json);
                if (saved != null) Current = Migrate(saved);
            }
            catch
            {
                // Повреждённый файл не останавливает запуск. Дефолты будут
                // перезаписаны после генерации токена.
                Current = new Settings();
            }
        }

        if (string.IsNullOrWhiteSpace(Current.Token) || Current.Token == "yawa_demo")
            Current.Token = "yawa_" + Guid.NewGuid().ToString("N")[..16];
        Current = Migrate(Current);
        SaveNoLock();
    }

    private static Settings Migrate(Settings settings)
    {
        settings.Tts ??= new TtsConfig();
        settings.Tts.Template ??= new Dictionary<string, bool>();
        settings.Tts.Filters ??= new Dictionary<string, object>();
        settings.Overlay ??= new OverlayConfig();
        settings.ChatView ??= new Dictionary<string, object>();
        settings.Widget ??= new Dictionary<string, object>();
        settings.Hotkeys ??= new Dictionary<string, string>();
        foreach (var hotkey in new Settings().Hotkeys)
            if (!settings.Hotkeys.ContainsKey(hotkey.Key)) settings.Hotkeys[hotkey.Key] = hotkey.Value;
        settings.Channels ??= new List<Channel>();
        if (settings.SettingsSchemaVersion < 3) settings.CloseToTray = false;
        settings.SettingsSchemaVersion = 4;
        return settings;
    }

    public void Patch(string patchJson)
    {
        Settings? changed = null;
        lock (_lock)
        {
            try
            {
                var patch = JsonNode.Parse(patchJson) as JsonObject;
                if (patch == null) return;

                // Token нельзя перезаписать patch-ом фронтенда.
                patch.Remove("token");

                var current = JsonNode.Parse(JsonSerializer.Serialize(Current, _json)) as JsonObject;
                if (current == null) return;
                DeepMerge(current, patch);

                var next = JsonSerializer.Deserialize<Settings>(current.ToJsonString(), _json);
                if (next == null) return;
                next.Token = Current.Token;
                Current = Migrate(next);
                SaveNoLock();
                changed = Current;
            }
            catch
            {
                // Некорректный patch не ломает уже сохранённые настройки.
                return;
            }
        }
        if (changed != null) Changed?.Invoke(changed);
    }

    private static void DeepMerge(JsonObject target, JsonObject patch)
    {
        foreach (var item in patch)
        {
            if (item.Value is JsonObject patchObject && target[item.Key] is JsonObject targetObject)
            {
                DeepMerge(targetObject, patchObject);
            }
            else
            {
                target[item.Key] = item.Value?.DeepClone();
            }
        }
    }

    public void PatchOverlayBounds(Bounds bounds)
    {
        lock (_lock)
        {
            Current.OverlayBounds = bounds;
            SaveNoLock();
        }
        // Bounds меняются много раз во время drag/resize. Не рассылаем общий
        // SettingsChanged на каждый пиксель — иначе OverlayManager повторно
        // применяет конфиг и мешает перемещению окна.
    }

    public string ToJson()
    {
        lock (_lock) return JsonSerializer.Serialize(Current, _json);
    }

    private void SaveNoLock()
    {
        try
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, _json));
            File.Move(tmp, _path, true);
        }
        catch { }
    }
}

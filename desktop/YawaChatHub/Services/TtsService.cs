using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Speech.Synthesis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace YawaChatHub.Services;

public class TtsItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("rate")] public double Rate { get; set; } = 1.0;
    [JsonPropertyName("volume")] public double Volume { get; set; } = 0.9;
    [JsonPropertyName("voice")] public string? Voice { get; set; }
}

/// <summary>
/// Очередь озвучки SAPI5 + SSML с принудительным xml:lang="ru-RU" (ТЗ §16.3).
/// FIFO, максимум 12; один постоянный процесс синтеза.
/// </summary>
public sealed class TtsService : IDisposable
{
    private static readonly Lazy<TtsService> _lazy = new(() => new TtsService());
    public static TtsService Instance => _lazy.Value;

    private readonly SpeechSynthesizer _synth = new();
    private readonly ConcurrentQueue<TtsItem> _queue = new();
    private volatile bool _busy;

    private TtsService() { }

    public void Enqueue(string json)
    {
        try
        {
            var item = JsonSerializer.Deserialize<TtsItem>(json);
            if (item == null || string.IsNullOrWhiteSpace(item.Text)) return;
            _queue.Enqueue(item);
            while (_queue.Count > 12) _queue.TryDequeue(out _);
            if (!_busy) ProcessQueue();
        }
        catch { }
    }

    private async void ProcessQueue()
    {
        if (_busy || !_queue.TryDequeue(out var item)) return;
        _busy = true;
        try
        {
            var ssml = BuildSsml(item);
            await _synth.SpeakSsmlAsync(ssml);
        }
        catch
        {
            // Откат на простой Speak, если голос не поддерживает SSML
            try { _synth.Speak(item.Text); } catch { }
        }
        _busy = false;
        BridgeHost.Current?.EmitTtsEnd(item.Id);
        ProcessQueue();
    }

    private static string BuildSsml(TtsItem item)
    {
        var escaped = XmlUtil.EscapeXml(item.Text);
        var ratePct = (int)(item.Rate * 100 - 100);
        var vol = Math.Clamp((int)(item.Volume * 100), 0, 100);
        var prosody = $"<prosody rate=\"{ratePct:+#;-#;0}%\" volume=\"{vol}\">";
        if (!string.IsNullOrEmpty(item.Voice))
            return $"<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"ru-RU\"><voice name=\"{XmlUtil.EscapeXml(item.Voice)}\" xml:lang=\"ru-RU\">{prosody}{escaped}</prosody></voice></speak>";
        return $"<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"ru-RU\">{prosody}{escaped}</prosody></speak>";
    }

    public List<string> Voices()
    {
        var names = new List<string>();
        using var s = new SpeechSynthesizer();
        names.AddRange(s.GetInstalledVoices().Select(v => v.VoiceInfo.Name));
        return names.Distinct().OrderBy(n => n, new RuVoiceComparer()).ToList();
    }

    public void Skip()
    {
        try { _synth.SpeakAsyncCancelAll(); } catch { }
    }

    public void StopAll()
    {
        while (_queue.TryDequeue(out _)) { }
        try { _synth.SpeakAsyncCancelAll(); } catch { }
    }

    public void Dispose() => _synth.Dispose();

    /// <summary>Русские голоса первыми (аналог RuVoiceComparer).</summary>
    private sealed class RuVoiceComparer : IComparer<string>
    {
        private static int Rank(string n) =>
            n.Contains("ru-RU", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Russian", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

        public int Compare(string? x, string? y)
        {
            var r = Rank(x ?? "").CompareTo(Rank(y ?? ""));
            return r != 0 ? r : string.Compare(x, y, StringComparison.Ordinal);
        }
    }
}

// Xml-экранирование без полной зависимости от System.Security
internal static class XmlUtil
{
    public static string EscapeXml(string s)
    {
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&apos;");
    }
}

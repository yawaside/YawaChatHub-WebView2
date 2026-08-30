using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Speech.Synthesis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

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
/// FIFO, максимум 12, один постоянный процесс синтеза.
///
/// ВАЖНО: синтез идёт в ФОНОВОМ потоке. Раньше SpeakSsml вызывался прямо из
/// JS-моста на UI-потоке и подвешивал всё окно на время произнесения фразы.
/// </summary>
public sealed class TtsService : IDisposable
{
    private static readonly Lazy<TtsService> _lazy = new(() => new TtsService());
    public static TtsService Instance => _lazy.Value;

    private readonly SpeechSynthesizer _synth = new();
    private readonly ConcurrentQueue<TtsItem> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly ManualResetEventSlim _spoken = new(false);
    private volatile bool _disposed;

    private TtsService()
    {
        try { _synth.SetOutputToDefaultAudioDevice(); } catch { }
        _synth.SpeakCompleted += (_, _) => _spoken.Set();

        var worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "yawa-tts",
        };
        worker.Start();
    }

    public void Enqueue(string json)
    {
        try
        {
            var item = JsonSerializer.Deserialize<TtsItem>(json);
            if (item == null || string.IsNullOrWhiteSpace(item.Text)) return;

            _queue.Enqueue(item);
            while (_queue.Count > 12) _queue.TryDequeue(out _); // ТЗ §16.2
            _signal.Release();
        }
        catch { }
    }

    private void WorkerLoop()
    {
        while (!_disposed)
        {
            try
            {
                _signal.Wait();
                if (_disposed) return;
                if (!_queue.TryDequeue(out var item)) continue;

                Speak(item);
                BridgeHost.Current?.EmitTtsEnd(item.Id);
            }
            catch { /* очередь продолжает жить при любой ошибке */ }
        }
    }

    private void Speak(TtsItem item)
    {
        try
        {
            _synth.Volume = Math.Clamp((int)Math.Round(item.Volume * 100), 0, 100);
            _synth.Rate = Math.Clamp((int)Math.Round((item.Rate - 1.0) * 10), -10, 10);
        }
        catch { }

        _spoken.Reset();
        try
        {
            _synth.SpeakSsmlAsync(BuildSsml(item));
        }
        catch
        {
            // Откат на простой текст, если голос не поддерживает SSML
            try { _synth.SpeakAsync(item.Text); }
            catch { return; }
        }

        // Ждём завершения; Skip()/StopAll() досрочно снимут ожидание
        _spoken.Wait(TimeSpan.FromSeconds(60));
    }

    private static string BuildSsml(TtsItem item)
    {
        var escaped = XmlUtil.EscapeXml(item.Text);
        var voice = string.IsNullOrEmpty(item.Voice)
            ? ""
            : $"<voice name=\"{XmlUtil.EscapeXml(item.Voice)}\" xml:lang=\"ru-RU\">";
        var voiceEnd = string.IsNullOrEmpty(item.Voice) ? "" : "</voice>";
        return "<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"ru-RU\">"
             + voice + escaped + voiceEnd + "</speak>";
    }

    public List<string> Voices()
    {
        try
        {
            using var s = new SpeechSynthesizer();
            return s.GetInstalledVoices()
                    .Where(v => v.Enabled)
                    .Select(v => v.VoiceInfo.Name)
                    .Distinct()
                    .OrderBy(n => n, new RuVoiceComparer())
                    .ToList();
        }
        catch { return new List<string>(); }
    }

    public void Skip()
    {
        try { _synth.SpeakAsyncCancelAll(); } catch { }
        _spoken.Set();
    }

    public void StopAll()
    {
        while (_queue.TryDequeue(out _)) { }
        Skip();
    }

    public void Dispose()
    {
        _disposed = true;
        try { _signal.Release(); } catch { }
        try { _synth.SpeakAsyncCancelAll(); } catch { }
        try { _synth.Dispose(); } catch { }
    }

    /// <summary>Русские голоса первыми (аналог RuVoiceComparer).</summary>
    private sealed class RuVoiceComparer : IComparer<string>
    {
        private static int Rank(string n) =>
            n.Contains("ru-RU", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Russian", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Irina", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

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

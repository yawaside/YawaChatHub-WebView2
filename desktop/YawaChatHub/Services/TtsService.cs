using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security;
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
/// SAPI5 TTS: отдельный STA-поток, FIFO и максимум 12 сообщений (ТЗ §16).
/// Важно: SAPI никогда не запускается на UI/WebView-потоке, иначе окно
/// приложения зависает, пока произносится фраза.
/// </summary>
public sealed class TtsService : IDisposable
{
    private static readonly Lazy<TtsService> _lazy = new(() => new TtsService());
    public static TtsService Instance => _lazy.Value;

    private readonly ConcurrentQueue<TtsItem> _queue = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _worker;
    private SpeechSynthesizer? _synth;
    private volatile bool _disposed;

    private TtsService()
    {
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "YawaChatHub.SAPI5",
        };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    public void Enqueue(string json)
    {
        try
        {
            var item = JsonSerializer.Deserialize<TtsItem>(json);
            if (item == null || string.IsNullOrWhiteSpace(item.Text) || _disposed) return;
            _queue.Enqueue(item);
            while (_queue.Count > 12) _queue.TryDequeue(out _);
            _signal.Set();
        }
        catch { }
    }

    private void WorkerLoop()
    {
        try
        {
            _synth = new SpeechSynthesizer();
            while (!_stop.IsCancellationRequested)
            {
                _signal.WaitOne(250);
                while (!_stop.IsCancellationRequested && _queue.TryDequeue(out var item))
                {
                    try
                    {
                        // SSML всегда ru-RU, как требуется в ТЗ.
                        _synth.SpeakSsml(BuildSsml(item));
                    }
                    catch
                    {
                        // Fallback для голоса без поддержки SSML.
                        try { _synth.Speak(item.Text); } catch { }
                    }
                    BridgeHost.Current?.EmitTtsEnd(item.Id);
                }
            }
        }
        catch { }
        finally
        {
            _synth?.Dispose();
            _synth = null;
        }
    }

    private static string BuildSsml(TtsItem item)
    {
        var escaped = SecurityElement.Escape(item.Text) ?? "";
        var ratePct = (int)(item.Rate * 100 - 100);
        var volume = Math.Clamp((int)(item.Volume * 100), 0, 100);
        var prosody = $"<prosody rate=\"{ratePct:+#;-#;0}%\" volume=\"{volume}\">";
        if (!string.IsNullOrWhiteSpace(item.Voice))
        {
            var voice = SecurityElement.Escape(item.Voice) ?? "";
            return $"<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"ru-RU\"><voice name=\"{voice}\" xml:lang=\"ru-RU\">{prosody}{escaped}</prosody></voice></speak>";
        }
        return $"<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"ru-RU\">{prosody}{escaped}</prosody></speak>";
    }

    public List<string> Voices()
    {
        var names = new List<string>();
        using var speech = new SpeechSynthesizer();
        names.AddRange(speech.GetInstalledVoices().Select(v => v.VoiceInfo.Name));
        return names.Distinct().OrderBy(n => n, new RuVoiceComparer()).ToList();
    }

    public void Skip()
    {
        try { _synth?.SpeakAsyncCancelAll(); } catch { }
    }

    public void StopAll()
    {
        while (_queue.TryDequeue(out _)) { }
        try { _synth?.SpeakAsyncCancelAll(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        _signal.Set();
        if (_worker.IsAlive) _worker.Join(1000);
        _signal.Dispose();
        _stop.Dispose();
    }

    private sealed class RuVoiceComparer : IComparer<string>
    {
        private static int Rank(string name) =>
            name.Contains("ru-RU", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Russian", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

        public int Compare(string? x, string? y)
        {
            var rank = Rank(x ?? "").CompareTo(Rank(y ?? ""));
            return rank != 0 ? rank : string.Compare(x, y, StringComparison.Ordinal);
        }
    }
}

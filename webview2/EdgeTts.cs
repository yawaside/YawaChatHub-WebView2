using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace YawaChatHub;

/// <summary>
/// Edge TTS — нейросетевой синтез речи Microsoft (те же голоса, что в браузере Edge).
/// Бесплатный, без ключей: WebSocket к speech.platform.bing.com, ответ — MP3-поток.
/// Здесь оставлены ТОЛЬКО русские голоса.
/// </summary>
public sealed class EdgeTts : IDisposable
{
    public sealed record Voice(string Id, string Title, string Gender);

    /// Русские нейроголоса Edge (ru-RU).
    public static readonly Voice[] RussianVoices =
    {
        new("ru-RU-SvetlanaNeural", "Светлана (женский, нейро)", "Female"),
        new("ru-RU-DmitryNeural",   "Дмитрий (мужской, нейро)",  "Male"),
        new("ru-RU-DariyaNeural",   "Дария (женский, нейро)",    "Female"),
    };

    public const string DefaultVoice = "ru-RU-SvetlanaNeural";

    private const string TrustedToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private static readonly string WsUrl =
        "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1" +
        $"?TrustedClientToken={TrustedToken}";

    private readonly string _cacheDir = Path.Combine(Program.AppDir, "tts-cache");
    private CancellationTokenSource? _current;

    public EdgeTts() => Directory.CreateDirectory(_cacheDir);

    /// <summary>Синтез речи и воспроизведение. rate/volume — проценты (-50..100 / 0..100).</summary>
    public async Task SpeakAsync(string text, string voice, int ratePercent, int volumePercent, CancellationToken outer = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (string.IsNullOrWhiteSpace(voice) || !voice.StartsWith("ru-RU", StringComparison.OrdinalIgnoreCase))
            voice = DefaultVoice;

        _current?.Cancel();
        _current = CancellationTokenSource.CreateLinkedTokenSource(outer);
        var ct = _current.Token;

        var mp3 = await SynthesizeAsync(text, voice, ratePercent, ct);
        if (mp3.Length == 0 || ct.IsCancellationRequested) return;

        var file = Path.Combine(_cacheDir, $"tts-{Guid.NewGuid():N}.mp3");
        await File.WriteAllBytesAsync(file, mp3, ct);
        try { await AudioPlayer.PlayAsync(file, volumePercent, ct); }
        finally { try { File.Delete(file); } catch { } }
    }

    public void Cancel()
    {
        try { _current?.Cancel(); } catch { }
        AudioPlayer.Stop();
    }

    /// Запрос синтеза по протоколу Edge Read Aloud.
    private static async Task<byte[]> SynthesizeAsync(string text, string voice, int ratePercent, CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpahbcnkgabbfbnjecoanbpk");
        ws.Options.SetRequestHeader("User-Agent", Net.UA);
        await ws.ConnectAsync(new Uri($"{WsUrl}&ConnectionId={Guid.NewGuid():N}"), ct);

        var stamp = DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'");

        // 1) конфиг выходного формата
        var config =
            $"X-Timestamp:{stamp}\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n" +
            "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":" +
            "{\"sentenceBoundaryEnabled\":false,\"wordBoundaryEnabled\":false}," +
            "\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}";
        await Send(ws, config, ct);

        // 2) SSML
        var rate = Math.Clamp(ratePercent, -50, 100);
        var ssml =
            $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='ru-RU'>" +
            $"<voice name='{voice}'><prosody rate='{(rate >= 0 ? "+" : "")}{rate}%' pitch='+0Hz'>" +
            $"{Escape(text)}</prosody></voice></speak>";
        var request =
            $"X-RequestId:{Guid.NewGuid():N}\r\nContent-Type:application/ssml+xml\r\n" +
            $"X-Timestamp:{stamp}Z\r\nPath:ssml\r\n\r\n{ssml}";
        await Send(ws, request, ct);

        // 3) чтение аудио: бинарные фреймы вида [2 байта длины заголовка][заголовок][mp3]
        using var audio = new MemoryStream();
        var buffer = new byte[16384];
        while (!ct.IsCancellationRequested)
        {
            using var frame = new MemoryStream();
            WebSocketReceiveResult res;
            do
            {
                res = await ws.ReceiveAsync(buffer, ct);
                if (res.MessageType == WebSocketMessageType.Close) goto done;
                frame.Write(buffer, 0, res.Count);
            } while (!res.EndOfMessage);

            var data = frame.ToArray();
            if (res.MessageType == WebSocketMessageType.Text)
            {
                if (Encoding.UTF8.GetString(data).Contains("Path:turn.end")) break;
                continue;
            }
            if (data.Length < 2) continue;
            int headerLen = (data[0] << 8) | data[1];
            int start = 2 + headerLen;
            if (start < data.Length) audio.Write(data, start, data.Length - start);
        }
    done:
        try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        return audio.ToArray();
    }

    private static Task Send(ClientWebSocket ws, string payload, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, ct);

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    public void Dispose()
    {
        Cancel();
        try
        {
            foreach (var f in Directory.GetFiles(_cacheDir, "tts-*.mp3")) File.Delete(f);
        }
        catch { }
    }

    // подавление предупреждения об неиспользуемом импорте криптографии
    private static readonly HashAlgorithm? _unused = null;
}

/// <summary>Проигрывание MP3 через Windows Media Player COM (без внешних зависимостей).</summary>
internal static class AudioPlayer
{
    private static dynamic? _player;
    private static readonly object _gate = new();

    public static Task PlayAsync(string file, int volumePercent, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                lock (_gate)
                {
                    _player ??= Activator.CreateInstance(
                        Type.GetTypeFromProgID("WMPlayer.OCX", throwOnError: true)!);
                }
                var p = _player!;
                p.settings.volume = Math.Clamp(volumePercent, 0, 100);
                p.URL = file;
                p.controls.play();

                // ждём завершения: playState 1 = Stopped, 8 = MediaEnded
                var started = DateTime.UtcNow;
                while (!ct.IsCancellationRequested)
                {
                    Thread.Sleep(120);
                    int state = (int)p.playState;
                    if (state is 8 or 1 && (DateTime.UtcNow - started).TotalMilliseconds > 400) break;
                    if ((DateTime.UtcNow - started).TotalMinutes > 3) break;   // страховка
                }
                try { p.controls.stop(); } catch { }
            }
            catch { /* аудио недоступно */ }
            tcs.TrySetResult();
        })
        { IsBackground = true, Name = "yawa-tts-play" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    public static void Stop()
    {
        try { lock (_gate) _player?.controls.stop(); } catch { }
    }
}

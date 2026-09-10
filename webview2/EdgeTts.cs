using System.Net.WebSockets;
using System.Text;
using NAudio.Wave;

namespace YawaChatHub;

/// <summary>
/// Edge TTS — нейросетевой синтез Microsoft (голоса браузера Edge), бесплатно и без ключей.
/// Только русские голоса.
///
/// Оптимизация задержки:
///  • WebSocket держится открытым и прогревается при старте приложения
///    (первый холодный коннект ~600 мс уходит в фон, а не в первое сообщение);
///  • аудио играется прямо из памяти через NAudio — без временных файлов
///    и без Windows Media Player COM, который стартовал больше секунды;
///  • устройство вывода создаётся один раз и переиспользуется.
/// </summary>
public sealed class EdgeTts : IDisposable
{
    public sealed record Voice(string Id, string Title, string Gender);

    public static readonly Voice[] RussianVoices =
    {
        new("ru-RU-SvetlanaNeural", "Светлана (женский, нейро)", "Female"),
        new("ru-RU-DmitryNeural",   "Дмитрий (мужской, нейро)",  "Male"),
        new("ru-RU-DariyaNeural",   "Дария (женский, нейро)",    "Female"),
    };

    public const string DefaultVoice = "ru-RU-SvetlanaNeural";

    private const string TrustedToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string WsBase =
        "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1" +
        "?TrustedClientToken=" + TrustedToken;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _playCts;
    private WaveOutEvent? _output;

    /// Прогрев: открываем соединение заранее, чтобы первая фраза звучала сразу.
    public void Warmup()
    {
        _ = Task.Run(async () =>
        {
            try { await EnsureSocketAsync(CancellationToken.None); } catch { }
        });
    }

    private async Task<ClientWebSocket> EnsureSocketAsync(CancellationToken ct)
    {
        if (_ws is { State: WebSocketState.Open }) return _ws;

        try { _ws?.Abort(); } catch { }
        _ws?.Dispose();

        var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpahbcnkgabbfbnjecoanbpk");
        ws.Options.SetRequestHeader("User-Agent", Net.UA);
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await ws.ConnectAsync(new Uri($"{WsBase}&ConnectionId={Guid.NewGuid():N}"), ct);

        var stamp = Stamp();
        await Send(ws,
            $"X-Timestamp:{stamp}\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n" +
            "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":" +
            "{\"sentenceBoundaryEnabled\":false,\"wordBoundaryEnabled\":false}," +
            "\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}", ct);

        _ws = ws;
        return ws;
    }

    /// <summary>Синтез и воспроизведение. rate — проценты (-50..100), volume — 0..100.</summary>
    public async Task SpeakAsync(string text, string voice, int ratePercent, int volumePercent, CancellationToken outer = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (string.IsNullOrWhiteSpace(voice) || !voice.StartsWith("ru-RU", StringComparison.OrdinalIgnoreCase))
            voice = DefaultVoice;

        byte[] mp3;
        await _gate.WaitAsync(outer);
        try
        {
            mp3 = await SynthesizeAsync(text, voice, ratePercent, outer);
        }
        catch
        {
            // соединение могло протухнуть — один повтор на свежем сокете
            try { _ws?.Abort(); } catch { }
            _ws = null;
            try { mp3 = await SynthesizeAsync(text, voice, ratePercent, outer); }
            catch { return; }
        }
        finally { _gate.Release(); }

        if (mp3.Length == 0 || outer.IsCancellationRequested) return;
        await PlayAsync(mp3, volumePercent, outer);
    }

    private async Task<byte[]> SynthesizeAsync(string text, string voice, int ratePercent, CancellationToken ct)
    {
        var ws = await EnsureSocketAsync(ct);

        var rate = Math.Clamp(ratePercent, -50, 100);
        var ssml =
            "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='ru-RU'>" +
            $"<voice name='{voice}'><prosody rate='{(rate >= 0 ? "+" : "")}{rate}%' pitch='+0Hz'>" +
            $"{Escape(text)}</prosody></voice></speak>";

        await Send(ws,
            $"X-RequestId:{Guid.NewGuid():N}\r\nContent-Type:application/ssml+xml\r\n" +
            $"X-Timestamp:{Stamp()}Z\r\nPath:ssml\r\n\r\n{ssml}", ct);

        using var audio = new MemoryStream();
        var buffer = new byte[16384];
        while (!ct.IsCancellationRequested)
        {
            using var frame = new MemoryStream();
            WebSocketReceiveResult res;
            do
            {
                res = await ws.ReceiveAsync(buffer, ct);
                if (res.MessageType == WebSocketMessageType.Close)
                    throw new InvalidOperationException("edge tts: сервер закрыл соединение");
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
        return audio.ToArray();
    }

    /// Проигрывание MP3 прямо из памяти (NAudio) — старт почти мгновенный.
    private Task PlayAsync(byte[] mp3, int volumePercent, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _playCts?.Cancel();
        _playCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _playCts.Token;

        _ = Task.Run(() =>
        {
            try
            {
                using var ms = new MemoryStream(mp3);
                using var reader = new Mp3FileReader(ms);
                _output?.Dispose();
                _output = new WaveOutEvent { DesiredLatency = 120 };
                _output.Init(reader);
                _output.Volume = Math.Clamp(volumePercent, 0, 100) / 100f;
                _output.Play();

                while (_output.PlaybackState == PlaybackState.Playing)
                {
                    if (token.IsCancellationRequested) { _output.Stop(); break; }
                    Thread.Sleep(40);
                }
            }
            catch { /* аудиоустройство недоступно */ }
            finally { tcs.TrySetResult(); }
        }, CancellationToken.None);

        return tcs.Task;
    }

    public void Cancel()
    {
        try { _playCts?.Cancel(); } catch { }
        try { _output?.Stop(); } catch { }
    }

    private static string Stamp() =>
        DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'");

    private static Task Send(ClientWebSocket ws, string payload, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, ct);

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    public void Dispose()
    {
        Cancel();
        try { _output?.Dispose(); } catch { }
        try { _ws?.Abort(); _ws?.Dispose(); } catch { }
        _gate.Dispose();
    }
}

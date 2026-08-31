using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace YawaChatHub.Services;

public enum ChannelState
{
    Connecting,
    Online,
    Offline,
    Unsupported,
}

/// <summary>
/// Коннекторы площадок (ТЗ §6). Протоколы взяты из рабочей Electron-версии:
///   Twitch — анонимный IRC поверх WebSocket (wss://irc-ws.chat.twitch.tv:443);
///   Kick   — Pusher WebSocket + kick.com/api/v2/channels/{slug};
///   YouTube — innertube live_chat без Data API и без ключа.
/// VK/TikTok требуют подписанных Node-хелперов и честно помечаются как
/// «нет коннектора» — приложение не выдаёт их за подключённые.
/// Каждый коннектор работает независимо и переподключается сам.
/// </summary>
public sealed class ConnectorManager
{
    private static readonly Lazy<ConnectorManager> _lazy = new(() => new ConnectorManager());
    public static ConnectorManager Instance => _lazy.Value;

    private readonly object _lock = new();
    private readonly Dictionary<string, IConnector> _connectors = new();
    private readonly Dictionary<string, ChannelState> _state = new();
    private readonly List<Channel> _channels = new();

    private ConnectorManager() { }

    private static string Key(string platform, string channel) => $"{platform}:{channel}";

    public List<Channel> List()
    {
        lock (_lock) return _channels.ToList();
    }

    public void Add(string platform, string channelId)
    {
        lock (_lock)
        {
            if (_channels.Any(c => c.Platform == platform && c.ChannelId == channelId)) return;
            _channels.Add(new Channel { Platform = platform, ChannelId = channelId });
        }

        var supported = platform is "twitch" or "kick" or "youtube";
        SetState(platform, channelId, supported ? ChannelState.Connecting : ChannelState.Unsupported);
        if (supported) StartConnector(platform, channelId);

        BridgeHost.Current?.EmitChannels(List());
    }

    public void Remove(string platform, string channelId)
    {
        IConnector? connector;
        lock (_lock)
        {
            _channels.RemoveAll(c => c.Platform == platform && c.ChannelId == channelId);
            _state.Remove(Key(platform, channelId));
            _connectors.TryGetValue(Key(platform, channelId), out connector);
            _connectors.Remove(Key(platform, channelId));
        }
        // Останавливаем рабочий цикл — иначе отключённый канал продолжит
        // присылать сообщения в ленту.
        connector?.Stop();
        BridgeHost.Current?.EmitChannels(List());
    }

    public void SetState(string platform, string channelId, ChannelState state)
    {
        lock (_lock)
        {
            if (_state.TryGetValue(Key(platform, channelId), out var prev) && prev == state) return;
            _state[Key(platform, channelId)] = state;
        }
        BridgeHost.Current?.EmitChannelStatus(platform, channelId, state);
    }

    public void Diagnose() { }

    private void StartConnector(string platform, string channelId)
    {
        IConnector c = platform switch
        {
            "twitch" => new TwitchConnector(channelId),
            "kick" => new KickConnector(channelId),
            "youtube" => new YoutubeConnector(channelId),
            _ => new StubConnector(),
        };
        c.StateChanged += state => SetState(platform, channelId, state);
        lock (_lock) _connectors[Key(platform, channelId)] = c;
        _ = Task.Run(c.Run);
    }
}

public interface IConnector
{
    Task Run();
    void Stop();
    event Action<ChannelState>? StateChanged;
}

/// <summary>Площадка без реализованного коннектора — молчит и не «подключается».</summary>
public sealed class StubConnector : IConnector
{
#pragma warning disable CS0067
    public event Action<ChannelState>? StateChanged;
#pragma warning restore CS0067
    public Task Run() => Task.CompletedTask;
    public void Stop() { }
}

/// <summary>Базовый цикл с переподключением и растущей паузой.</summary>
public abstract class ConnectorBase : IConnector
{
    protected readonly CancellationTokenSource Cts = new();
    public event Action<ChannelState>? StateChanged;

    protected void Report(ChannelState state) => StateChanged?.Invoke(state);

    public void Stop()
    {
        try { Cts.Cancel(); } catch { }
    }

    public async Task Run()
    {
        var delay = 3000;
        while (!Cts.IsCancellationRequested)
        {
            try
            {
                await RunOnce(Cts.Token);
                delay = 3000;
            }
            catch (OperationCanceledException) { return; }
            catch { /* сеть/протокол — переподключаемся */ }

            if (Cts.IsCancellationRequested) return;
            Report(ChannelState.Connecting);
            try { await Task.Delay(delay, Cts.Token); } catch { return; }
            delay = Math.Min(delay * 2, 30000);
        }
    }

    protected abstract Task RunOnce(CancellationToken ct);

    protected static async Task SendAsync(ClientWebSocket ws, string text, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    /// <summary>Читает текстовые кадры WebSocket (склеивает фрагменты).</summary>
    protected static async IAsyncEnumerable<string> ReadAsync(
        ClientWebSocket ws,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var sb = new StringBuilder();
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try { result = await ws.ReceiveAsync(buffer, ct); }
            catch { yield break; }

            if (result.MessageType == WebSocketMessageType.Close) yield break;
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage) continue;

            var text = sb.ToString();
            sb.Clear();
            yield return text;
        }
    }
}

/// <summary>
/// Twitch: анонимный IRC поверх WebSocket. Именно WS-эндпоинт (не TCP:6697)
/// стабильно работает из десктоп-приложения. Логин justinfan* + PASS SCHMOOPIIFS.
/// </summary>
public sealed class TwitchConnector : ConnectorBase
{
    private static readonly Regex PrivMsg =
        new(@"^(?:@(?<tags>[^ ]+) )?:(?<nick>[^!]+)![^ ]+ PRIVMSG #(?<chan>\S+) :(?<text>.*)$",
            RegexOptions.Compiled);

    private readonly string _channel;
    private int _watcherStarted;

    public TwitchConnector(string channel) => _channel = channel.TrimStart('#', '@').ToLowerInvariant();

    protected override async Task RunOnce(CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("User-Agent", Net.UA);
        await ws.ConnectAsync(new Uri("wss://irc-ws.chat.twitch.tv:443"), ct);

        await SendAsync(ws, "CAP REQ :twitch.tv/tags twitch.tv/commands\r\n", ct);
        await SendAsync(ws, "PASS SCHMOOPIIFS\r\n", ct);
        await SendAsync(ws, $"NICK justinfan{Random.Shared.Next(10000, 99999)}\r\n", ct);
        await SendAsync(ws, $"JOIN #{_channel}\r\n", ct);

        // Наблюдатель статуса запускается один раз на весь жизненный цикл,
        // а не при каждом переподключении.
        if (Interlocked.Exchange(ref _watcherStarted, 1) == 0)
            _ = Task.Run(() => WatchLiveAsync(Cts.Token), Cts.Token);

        await foreach (var frame in ReadAsync(ws, ct))
        {
            foreach (var line in frame.Split('\n'))
            {
                var s = line.TrimEnd('\r');
                if (s.Length == 0) continue;

                if (s.StartsWith("PING", StringComparison.Ordinal))
                {
                    await SendAsync(ws, "PONG :tmi.twitch.tv\r\n", ct);
                    continue;
                }

                var m = PrivMsg.Match(s);
                if (!m.Success) continue;

                var author = m.Groups["nick"].Value;
                var text = m.Groups["text"].Value;
                var tags = ParseTags(m.Groups["tags"].Value);

                var msg = ChatMsg.CreateText("twitch", _channel, author, text,
                    tags.TryGetValue("color", out var c) && !string.IsNullOrEmpty(c) ? c : "#9146FF");
                msg.Badges.AddRange(Badges(tags));
                BridgeHost.Current?.EmitChat(msg);
            }
        }
    }

    /// <summary>Онлайн-статус стримера — по публичной странице канала.</summary>
    private async Task WatchLiveAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var html = await Net.GetTextAsync($"https://www.twitch.tv/{_channel}", null, ct);
            var live = html.Contains("\"isLiveBroadcast\":true") ||
                       html.Contains("isLiveBroadcast\":true");
            Report(live ? ChannelState.Online : ChannelState.Offline);
            try { await Task.Delay(TimeSpan.FromSeconds(60), ct); } catch { return; }
        }
    }

    private static Dictionary<string, string> ParseTags(string raw)
    {
        var tags = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(raw)) return tags;
        foreach (var part in raw.Split(';'))
        {
            var i = part.IndexOf('=');
            if (i < 0) continue;
            tags[part[..i]] = part[(i + 1)..].Replace("\\s", " ").Replace("\\:", ";");
        }
        return tags;
    }

    private static IEnumerable<string> Badges(Dictionary<string, string> tags)
    {
        var badges = tags.TryGetValue("badges", out var b) ? b : "";
        if (tags.GetValueOrDefault("mod") == "1" || badges.Contains("moderator/") || badges.Contains("broadcaster/"))
            yield return "mod";
        if (badges.Contains("vip/")) yield return "vip";
        if (tags.GetValueOrDefault("subscriber") == "1" || badges.Contains("subscriber/")) yield return "sub";
    }
}

/// <summary>
/// Kick: данные канала через kick.com/api/v2/channels/{slug}, сообщения —
/// через Pusher WebSocket (тот же транспорт, что и у сайта).
/// </summary>
public sealed class KickConnector : ConnectorBase
{
    private static readonly string[] PusherKeys = { "32cbd69e4b950bf97679", "eb1d5f283081a78b932c" };

    private readonly string _slug;
    private int _keyIndex;

    public KickConnector(string channel) => _slug = channel.TrimStart('#', '@').ToLowerInvariant();

    protected override async Task RunOnce(CancellationToken ct)
    {
        var info = await Net.GetJsonAsync($"https://kick.com/api/v2/channels/{_slug}",
            new[] { ("Accept", "application/json"), ("Referer", "https://kick.com/") }, ct);

        var chatroomId = info?["chatroom"]?["id"]?.ToString();
        if (string.IsNullOrEmpty(chatroomId))
        {
            // Cloudflare или неизвестный канал — не выдаём за подключение.
            Report(ChannelState.Connecting);
            throw new InvalidOperationException("Kick: не получен chatroom id");
        }

        // livestream == null → стример офлайн; объект → идёт эфир.
        Report(info?["livestream"] is JsonObject ? ChannelState.Online : ChannelState.Offline);

        var key = PusherKeys[_keyIndex % PusherKeys.Length];
        _keyIndex++;

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("User-Agent", Net.UA);
        ws.Options.SetRequestHeader("Origin", "https://kick.com");
        await ws.ConnectAsync(
            new Uri($"wss://ws-us2.pusher.com/app/{key}?protocol=7&client=js&version=8.4.0-rc2&flash=false"), ct);

        await SendAsync(ws, JsonSerializer.Serialize(new
        {
            @event = "pusher:subscribe",
            data = new { auth = "", channel = $"chatrooms.{chatroomId}.v2" },
        }), ct);

        await foreach (var frame in ReadAsync(ws, ct))
        {
            JsonNode? node;
            try { node = JsonNode.Parse(frame); } catch { continue; }

            var evt = node?["event"]?.ToString();
            if (evt == "pusher:ping")
            {
                await SendAsync(ws, "{\"event\":\"pusher:pong\",\"data\":{}}", ct);
                continue;
            }
            if (evt is null || !evt.Contains("ChatMessageEvent")) continue;

            var raw = node?["data"]?.ToString();
            if (string.IsNullOrEmpty(raw)) continue;

            JsonNode? payload;
            try { payload = JsonNode.Parse(raw); } catch { continue; }

            var text = payload?["content"]?.ToString();
            var author = payload?["sender"]?["username"]?.ToString();
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(author)) continue;

            var color = payload?["sender"]?["identity"]?["color"]?.ToString();
            var msg = ChatMsg.CreateText("kick", _slug, author, text,
                string.IsNullOrEmpty(color) ? "#53FC18" : color);

            if (payload?["sender"]?["identity"]?["badges"] is JsonArray badges)
            {
                foreach (var b in badges)
                {
                    var type = b?["type"]?.ToString() ?? "";
                    if (type.Contains("moderator")) msg.Badges.Add("mod");
                    else if (type.Contains("subscriber")) msg.Badges.Add("sub");
                    else if (type.Contains("vip") || type.Contains("broadcaster")) msg.Badges.Add("vip");
                }
            }
            BridgeHost.Current?.EmitChat(msg);
        }
    }
}

/// <summary>YouTube Live: поиск эфира + polling live_chat без Data API.</summary>
public sealed class YoutubeConnector : ConnectorBase
{
    private readonly string _handle;

    public YoutubeConnector(string channel)
    {
        var h = channel.Trim();
        if (!h.StartsWith("@")) h = "@" + h;
        _handle = h;
    }

    protected override async Task RunOnce(CancellationToken ct)
    {
        var videoId = await YoutubeChat.FindLiveVideoIdAsync(_handle, ct);
        if (string.IsNullOrEmpty(videoId))
        {
            // Эфира нет — это не ошибка: ждём и проверяем снова.
            Report(ChannelState.Offline);
            await Task.Delay(TimeSpan.FromSeconds(45), ct);
            return;
        }

        var session = await YoutubeChat.OpenChatAsync(videoId, ct);
        if (session == null)
        {
            Report(ChannelState.Offline);
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return;
        }

        Report(ChannelState.Online);

        while (!ct.IsCancellationRequested)
        {
            var poll = await YoutubeChat.PollAsync(session, ct);
            if (poll == null) return; // эфир закончился/токен истёк — внешний цикл повторит

            foreach (var item in poll.Value.Messages)
            {
                var msg = ChatMsg.CreateText("youtube", _handle, item.Author, item.Text, "#FF0033");
                msg.Badges.AddRange(item.Badges);
                BridgeHost.Current?.EmitChat(msg);
            }

            var wait = Math.Clamp(poll.Value.TimeoutMs, 1500, 10000);
            await Task.Delay(wait, ct);
        }
    }
}

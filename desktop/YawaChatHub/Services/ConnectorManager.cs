using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
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
/// Управляет подключениями к площадкам (ТЗ §6). Коннекторы только ЧИТАЮТ
/// публичные чаты: Twitch/Kick — IRC (частный чат), YouTube Live — поиск
/// эфира без API-ключа + polling live_chat. VK/TikTok пока не реализованы и
/// честно показывают «нет коннектора» вместо фейковых «подключено».
/// Статус стримера (online/offline) сообщается в UI событием sp:channel:status.
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

    private static string Key(string platform, string channel) =>
        $"{platform}:{channel}";

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

        var supported = platform is "twitch" or "kick" or "youtube" or "vk";
        SetState(platform, channelId, supported ? ChannelState.Connecting : ChannelState.Unsupported);

        if (supported)
            StartConnector(platform, channelId);

        BridgeHost.Current?.EmitChannels(List());
    }

    public void Remove(string platform, string channelId)
    {
        lock (_lock)
        {
            _channels.RemoveAll(c => c.Platform == platform && c.ChannelId == channelId);
            _state.Remove(Key(platform, channelId));
            _connectors.Remove(Key(platform, channelId));
        }
        BridgeHost.Current?.EmitChannels(List());
    }

    public void SetState(string platform, string channelId, ChannelState state)
    {
        lock (_lock) _state[Key(platform, channelId)] = state;
        BridgeHost.Current?.EmitChannelStatus(platform, channelId, state);
    }

    public void Diagnose()
    {
        // Диагностика не имитирует чат и ничего не вставляет в ленту.
    }

    private void StartConnector(string platform, string channelId)
    {
        IConnector c = platform switch
        {
            "twitch" => new IrcConnector("irc.chat.twitch.tv", 6697, "justinfan" + new Random().Next(10000, 99999), platform, channelId),
            "kick" => new KickConnector(channelId.TrimStart('@')),
            "youtube" => new YoutubeConnector(channelId.TrimStart('@')),
            "vk" => new VkPlayConnector(channelId.TrimStart('@')),
            _ => new StubConnector(platform, channelId),
        };
        c.StateChanged += state => SetState(platform, channelId, state);
        lock (_lock) _connectors[Key(platform, channelId)] = c;
        _ = Task.Run(() => c.Run());
    }
}

public interface IConnector
{
    Task Run();
    event Action<ChannelState>? StateChanged;
}

/// <summary>Плейсхолдер для нереализованной площадки — ничего не отправляет.</summary>
public sealed class StubConnector : IConnector
{
#pragma warning disable CS0067
    public event Action<ChannelState>? StateChanged;
#pragma warning restore CS0067
    public StubConnector(string platform, string channel) { }
    public Task Run() => Task.CompletedTask;
}

/// <summary>
/// Twitch и Kick по IRC (чтение публичного private-чата).
/// Статус стримера определяется из списка зрителей (353/366):
/// пустой канал = стример офлайн, есть зрители = онлайн.
/// </summary>
public sealed class IrcConnector : IConnector
{
    private static readonly Regex PrivMsg =
        new(@"^(?:@(?<tags>[^ ]+) )?:(?<nick>[^!]+)![^ ]+ PRIVMSG #(?<chan>\S+) :(?<text>.*)$",
            RegexOptions.Compiled);
    private static readonly Regex UserList = new(@"^:\S+ 353 ", RegexOptions.Compiled);
    private static readonly Regex EndOfNames = new(@"^:\S+ 366 ", RegexOptions.Compiled);

    private readonly string _host; private readonly int _port;
    private readonly string _nick; private readonly string _platform; private readonly string _channel;

    public event Action<ChannelState>? StateChanged;

    public IrcConnector(string host, int port, string nick, string platform, string channel)
    {
        _host = host; _port = port; _nick = nick; _platform = platform; _channel = channel.TrimStart('#').TrimStart('@');
    }

    public async Task Run()
    {
        var delay = 2000;
        while (true)
        {
            var namesDone = false;
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(_host, _port);
                using var ssl = new SslStream(tcp.GetStream(), false,
                    (_, _, _, _) => true); // корпоративный прокси с TLS-инспекцией
                await ssl.AuthenticateAsClientAsync(_host);

                // Для анонимного чтения Twitch требует PASS SCHMOOPIIE.
                await Write(ssl, "PASS SCHMOOPIIE\r\n");
                await Write(ssl, $"NICK {_nick}\r\n");
                await Write(ssl, "CAP REQ :twitch.tv/tags twitch.tv/commands twitch.tv/membership\r\n");
                await Write(ssl, $"JOIN #{_channel}\r\n");

                var users = new StringBuilder();
                var reader = new StreamReader(ssl, Encoding.UTF8);
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (line.StartsWith("PING "))
                    { await Write(ssl, "PONG " + line[5..] + "\r\n"); continue; }

                    if (UserList.IsMatch(line))
                    {
                        var i = line.LastIndexOf(':');
                        if (i >= 0 && i + 1 < line.Length) users.Append(line[(i + 1)..]);
                        continue;
                    }

                    if (EndOfNames.IsMatch(line) && !namesDone)
                    {
                        namesDone = true;
                        StateChanged?.Invoke(users.Length > 0 ? ChannelState.Online : ChannelState.Offline);
                    }

                    var m = PrivMsg.Match(line);
                    if (!m.Success) continue;
                    var author = m.Groups["nick"].Value;
                    var text = m.Groups["text"].Value;
                    if (author.Equals(_nick, StringComparison.OrdinalIgnoreCase)) continue;
                    BridgeHost.Current?.EmitChat(ChatMsg.CreateText(_platform, _channel, author, text));
                }
            }
            catch { /* сеть упала — переподключение */ }

            if (!namesDone) StateChanged?.Invoke(ChannelState.Connecting);
            await Task.Delay(delay);
            delay = Math.Min(delay * 2, 30000);
        }
    }

    private static async Task Write(SslStream s, string msg)
    {
        var b = Encoding.UTF8.GetBytes(msg);
        await s.WriteAsync(b, 0, b.Length);
        await s.FlushAsync();
    }
}

/// <summary>
/// Kick: публичный channel API + Pusher WebSocket (без авторизации).
/// Chatroom работает постоянно, а online/offline берётся из livestream.is_live.
/// </summary>
public sealed class KickConnector : IConnector
{
    private const string PusherUrl =
        "wss://ws-us2.pusher.com/app/32cbd69e4b950bf97679?protocol=7&client=js&version=8.4.0&flash=false";
    private const string Ua =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124.0.0.0 Safari/537.36";

    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
    }) { Timeout = TimeSpan.FromSeconds(25) };

    private readonly string _channel;
    public event Action<ChannelState>? StateChanged;

    public KickConnector(string channel) => _channel = channel.Trim().ToLowerInvariant();

    public async Task Run()
    {
        var delay = 2000;
        while (true)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get, $"https://kick.com/api/v2/channels/{Uri.EscapeDataString(_channel)}");
                request.Headers.UserAgent.ParseAdd(Ua);
                request.Headers.Referrer = new Uri($"https://kick.com/{_channel}");
                var response = await Http.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var info = JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject
                    ?? throw new InvalidOperationException("Kick API вернул пустой ответ");

                var room = info["chatroom"]?["id"]?.GetValue<long>() ?? 0;
                if (room <= 0) throw new InvalidOperationException("Kick chatroom.id не найден");
                var isLive = info["livestream"]?["is_live"]?.GetValue<bool>() ?? false;
                StateChanged?.Invoke(isLive ? ChannelState.Online : ChannelState.Offline);

                using var socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("Origin", "https://kick.com");
                await socket.ConnectAsync(new Uri(PusherUrl), CancellationToken.None);
                var subscribe = JsonSerializer.Serialize(new
                {
                    @event = "pusher:subscribe",
                    data = new { auth = "", channel = $"chatrooms.{room}.v2" },
                });
                await Send(socket, subscribe);
                delay = 2000;

                while (socket.State == WebSocketState.Open)
                {
                    var raw = await Receive(socket);
                    if (raw == null) break;
                    var envelope = JsonNode.Parse(raw) as JsonObject;
                    var eventName = envelope?["event"]?.GetValue<string>() ?? "";
                    if (eventName == "pusher:ping")
                    {
                        await Send(socket, "{\"event\":\"pusher:pong\",\"data\":{}}");
                        continue;
                    }
                    if (!eventName.Contains("ChatMessage", StringComparison.OrdinalIgnoreCase)) continue;

                    var dataRaw = envelope?["data"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(dataRaw)) continue;
                    var data = JsonNode.Parse(dataRaw) as JsonObject;
                    var message = data?["message"] as JsonObject ?? data;
                    var text = message?["content"]?.GetValue<string>()
                        ?? message?["message"]?.GetValue<string>();
                    var sender = message?["sender"] as JsonObject ?? data?["user"] as JsonObject;
                    var author = sender?["username"]?.GetValue<string>() ?? "Kick";
                    var color = sender?["identity"]?["color"]?.GetValue<string>() ?? "#53FC18";
                    if (!string.IsNullOrWhiteSpace(text))
                        BridgeHost.Current?.EmitChat(
                            ChatMsg.CreateText("kick", _channel, author, text, color));
                }
            }
            catch
            {
                StateChanged?.Invoke(ChannelState.Connecting);
            }
            await Task.Delay(delay);
            delay = Math.Min(delay * 2, 30000);
        }
    }

    private static async Task Send(ClientWebSocket socket, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<string?> Receive(ClientWebSocket socket)
    {
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

/// <summary>
/// VK Video Live / VK Play Live: публичный REST API без ключа. История чата
/// опрашивается короткими запросами; HashSet id исключает дубли.
/// </summary>
public sealed class VkPlayConnector : IConnector
{
    private const string Ua =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124.0.0.0 Safari/537.36";
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
    }) { Timeout = TimeSpan.FromSeconds(20) };

    private readonly string _channel;
    private readonly HashSet<long> _seen = new();
    private bool _first = true;
    public event Action<ChannelState>? StateChanged;

    public VkPlayConnector(string channel) => _channel = channel.Trim();

    public async Task Run()
    {
        while (true)
        {
            try
            {
                var meta = await Get($"https://api.vkplay.live/v1/blog/{Uri.EscapeDataString(_channel)}/public_video_stream?from=layer");
                var metaNode = JsonNode.Parse(meta) as JsonObject;
                var live = metaNode?["data"] is JsonArray data && data.Count > 0;
                StateChanged?.Invoke(live ? ChannelState.Online : ChannelState.Offline);

                if (live)
                {
                    var raw = await Get($"https://api.vkplay.live/v1/blog/{Uri.EscapeDataString(_channel)}/public_video_stream/chat?limit=100");
                    var root = JsonNode.Parse(raw) as JsonObject;
                    if (root?["data"] is JsonArray messages)
                    {
                        foreach (var node in messages.OfType<JsonObject>().OrderBy(m => m["createdAt"]?.GetValue<long>() ?? 0))
                        {
                            var id = node["id"]?.GetValue<long>() ?? 0;
                            if (id == 0 || !_seen.Add(id)) continue;
                            // При первом запросе запоминаем историю, но не вываливаем
                            // старые сообщения в только что подключённую ленту.
                            if (_first) continue;
                            var author = node["author"]?["displayName"]?.GetValue<string>()
                                ?? node["author"]?["nick"]?.GetValue<string>() ?? "VK";
                            var text = ParseParts(node["data"] as JsonArray);
                            if (!string.IsNullOrWhiteSpace(text))
                                BridgeHost.Current?.EmitChat(
                                    ChatMsg.CreateText("vk", _channel, author, text, "#0077FF"));
                        }
                    }
                    _first = false;
                }
            }
            catch
            {
                StateChanged?.Invoke(ChannelState.Connecting);
            }
            await Task.Delay(3000);
        }
    }

    private async Task<string> Get(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(Ua);
        request.Headers.Referrer = new Uri($"https://vkplay.live/{_channel}");
        var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static string ParseParts(JsonArray? parts)
    {
        if (parts == null) return "";
        var text = new StringBuilder();
        foreach (var part in parts.OfType<JsonObject>())
        {
            var type = part["type"]?.GetValue<string>() ?? "";
            if (type is "text" or "link")
                text.Append(ReadContent(part["content"]));
            else if (type == "mention")
                text.Append('@').Append(part["displayName"]?.GetValue<string>() ?? ReadContent(part["content"]));
            else if (type == "smile")
                text.Append(' ').Append(part["displayName"]?.GetValue<string>() ?? "смайл").Append(' ');
        }
        return text.ToString().Trim();
    }

    private static string ReadContent(JsonNode? content)
    {
        if (content is JsonValue value && value.GetValueKind() == JsonValueKind.String)
        {
            var raw = value.GetValue<string>() ?? "";
            try
            {
                if (JsonNode.Parse(raw) is JsonArray parsed && parsed.Count > 0)
                    return parsed[0]?.GetValue<string>() ?? raw;
            }
            catch { }
            return raw;
        }
        if (content is JsonArray array && array.Count > 0)
            return array[0]?.GetValue<string>() ?? "";
        return "";
    }
}

/// <summary>
/// YouTube Live (ТЗ: поиск эфира без API-ключа, polling чата):
/// 1) периодическая проверка страницы канала на «isLiveNow» → online/offline;
/// 2) если эфир идёт — нахождение live-видео и polling live_chat по
///    continuation (без Data API). Сообщения уходят тем же sp:chat.
/// </summary>
public sealed class YoutubeConnector : IConnector
{
    private const string Ua =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
    })
    { Timeout = TimeSpan.FromSeconds(25) };

    private readonly string _channel;
    private readonly HashSet<string> _seen = new();
    private bool _firstChatPage = true;
    private bool _running = true;

    public event Action<ChannelState>? StateChanged;

    public YoutubeConnector(string channel)
    {
        _channel = channel.TrimStart('@', '#');
    }

    public async Task Run()
    {
        var delay = 15_000;
        while (_running)
        {
            bool live = false;
            string? videoId = null;
            try
            {
                var html = await GetAsync($"https://www.youtube.com/@{_channel}");
                live = html.Contains("\"isLiveNow\":true") || html.Contains("\"isLiveContent\":true");
                videoId = live ? FindLiveVideoId(html) : null;
                StateChanged?.Invoke(live ? ChannelState.Online : ChannelState.Offline);
                delay = 15_000;
            }
            catch
            {
                StateChanged?.Invoke(ChannelState.Connecting);
            }

            if (live && videoId != null)
                await PollChat(videoId);

            if (!_running) return;
            await Task.Delay(delay);
            delay = Math.Min(delay * 2, 60_000);
        }
    }

    private async Task PollChat(string videoId)
    {
        try
        {
            while (_running)
            {
                var page = await GetAsync($"https://www.youtube.com/live_chat?is_popout=1&v={videoId}");
                var json = ExtractInitialData(page);
                if (json == null) return;

                foreach (var item in ParseMessages(json))
                {
                    var id = $"{item.Author}\n{item.Text}";
                    if (!_seen.Add(id)) continue;
                    if (_firstChatPage) continue;
                    BridgeHost.Current?.EmitChat(
                        ChatMsg.CreateText("youtube", _channel, item.Author, item.Text, item.Color));
                }
                _firstChatPage = false;
                if (_seen.Count > 2000) _seen.Clear();
                await Task.Delay(5000);
            }
        }
        catch
        {
            // Эфир завершился или временный сетевой сбой — внешний цикл
            // повторно определит статус и live video id.
        }
    }

    private static string? ExtractInitialData(string html)
    {
        var marker = "ytInitialData";
        var markerAt = html.IndexOf(marker, StringComparison.Ordinal);
        if (markerAt < 0) return null;
        var start = html.IndexOf('{', markerAt + marker.Length);
        if (start < 0) return null;
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < html.Length; i++)
        {
            var c = html[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '\"') inString = false;
                continue;
            }
            if (c == '\"') { inString = true; continue; }
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return html[start..(i + 1)];
        }
        return null;
    }

    private static string? FindLiveVideoId(string html)
    {
        foreach (Match m in Regex.Matches(html, "\"videoId\":\"([A-Za-z0-9_-]{11})\""))
        {
            var rest = html.Substring(m.Index, Math.Min(1600, html.Length - m.Index));
            if (rest.Contains("\"isLiveNow\":true")) return m.Groups[1].Value;
        }
        return null;
    }

    private static async Task<string> GetAsync(string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd(Ua);
        req.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8");
        var resp = await Http.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    private static async Task<string> PostAsync(string url, string json)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        req.Headers.UserAgent.ParseAdd(Ua);
        var resp = await Http.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    private static IEnumerable<(string Author, string Text, string Color)> ParseMessages(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root) yield break;
        foreach (var obj in FindByKey(root, "liveChatTextMessageRenderer"))
        {
            var text = ReadYoutubeText(obj["message"]);
            if (string.IsNullOrWhiteSpace(text)) continue;
            var author = ReadYoutubeText(obj["authorName"]) ?? "YouTube";
            var color = obj["authorColor"] is JsonValue cv && cv.GetValueKind() == JsonValueKind.String
                ? cv.GetValue<string>()!
                : "#FF0033";
            yield return (author, text, color);
        }
    }

    private static IEnumerable<JsonObject> FindByKey(JsonNode? node, string key)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue(key, out var found) && found is JsonObject foundObject)
                yield return foundObject;
            foreach (var value in obj.Select(p => p.Value))
            {
                foreach (var n in FindByKey(value, key)) yield return n;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var value in array)
            {
                foreach (var n in FindByKey(value, key)) yield return n;
            }
        }
    }

    private static string? ReadYoutubeText(JsonNode? node)
    {
        if (node is not JsonObject obj) return null;
        if (obj["simpleText"] is JsonValue simple && simple.GetValueKind() == JsonValueKind.String)
            return simple.GetValue<string>();
        if (obj["runs"] is not JsonArray runs) return null;
        var text = new StringBuilder();
        foreach (var run in runs.OfType<JsonObject>())
        {
            if (run["text"] is JsonValue value && value.GetValueKind() == JsonValueKind.String)
                text.Append(value.GetValue<string>());
            else if (run["emoji"]?["shortcuts"] is JsonArray shortcuts && shortcuts.Count > 0)
                text.Append(shortcuts[0]?.GetValue<string>() ?? "");
        }
        return text.ToString();
    }

    private static string? ExtractContinuation(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root) return null;
        var tokens = new List<string>();
        CollectTokens(root, tokens);
        return tokens.Count > 0 ? tokens[^1] : null;
    }

    private static void CollectTokens(JsonNode? node, List<string> acc)
    {
        if (node is JsonObject obj)
        {
            if (obj["token"] is JsonValue tv && tv.GetValueKind() == JsonValueKind.String
                && tv.GetValue<string>()!.Length > 20)
                acc.Add(tv.GetValue<string>()!);
            foreach (var value in obj.Select(p => p.Value)) CollectTokens(value, acc);
        }
        else if (node is JsonArray array)
        {
            foreach (var value in array) CollectTokens(value, acc);
        }
    }
}

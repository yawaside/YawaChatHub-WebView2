using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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

        var supported = platform is "twitch" or "kick" or "youtube";
        SetState(platform, channelId, supported ? ChannelState.Connecting : ChannelState.Unsupported);

        if (supported)
            StartConnector(platform, channelId);

        BridgeHost.Current?.EmitChannels(List());
    }

    public void Remove(string platform, string channelId)
    {
        IConnector? connector;
        lock (_lock)
        {
            _channels.RemoveAll(c => c.Platform == platform && c.ChannelId == channelId);
            _state.Remove(Key(platform, channelId));
            connector = _connectors.TryGetValue(Key(platform, channelId), out var c) ? c : null;
            _connectors.Remove(Key(platform, channelId));
        }
        // Обязательно останавливаем рабочий цикл коннектора — иначе отключённый
        // канал продолжит присылать сообщения.
        connector?.Stop();
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
            "kick" => new IrcConnector("irc.kick.com", 6697, "yawachat_" + new Random().Next(1000, 9999), platform, channelId),
            "youtube" => new YoutubeConnector(channelId.TrimStart('@')),
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
    void Stop();
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
    public void Stop() { }
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
    private volatile bool _running = true;

    public event Action<ChannelState>? StateChanged;

    public IrcConnector(string host, int port, string nick, string platform, string channel)
    {
        _host = host; _port = port; _nick = nick; _platform = platform; _channel = channel.TrimStart('#').TrimStart('@');
    }

    public void Stop() => _running = false;

    public async Task Run()
    {
        var delay = 2000;
        while (_running)
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
                // Для Kick анонимный вход работает без PASS или с "oauth:anonymous".
                if (_platform == "kick")
                {
                    await Write(ssl, $"NICK {_nick}\r\n");
                }
                else
                {
                    await Write(ssl, "PASS SCHMOOPIIE\r\n");
                    await Write(ssl, $"NICK {_nick}\r\n");
                }
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
    private volatile bool _running = true;

    public event Action<ChannelState>? StateChanged;

    public YoutubeConnector(string channel)
    {
        // Сохраняем handle как есть: для страницы канала YouTube нужен формат
        // https://www.youtube.com/@handle (без «@» адрес не резолвится).
        var c = channel.Trim();
        if (!c.StartsWith("@")) c = "@" + c;
        _channel = c;
    }

    public void Stop() => _running = false;

    public async Task Run()
    {
        var delay = 15_000;
        while (_running)
        {
            bool live = false;
            string? videoId = null;
            try
            {
                var html = await GetAsync($"https://www.youtube.com/{_channel}");
                // В канальной странице флаг может отсутствовать — проверяем и
                // live-подстраницу канала.
                if (!html.Contains("\"isLiveNow\":true") && !html.Contains("\"isLiveContent\":true"))
                {
                    var liveHtml = await GetAsync($"https://www.youtube.com/{_channel}/live");
                    html += liveHtml;
                }
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
        string? token = null;
        try
        {
            while (_running)
            {
                if (token == null)
                {
                    var page = await GetAsync($"https://www.youtube.com/live_chat?is_popout=1&v={videoId}");
                    var matches = Regex.Matches(page, "\"continuation\":\"([^\"]{20,})\"");
                    if (matches.Count == 0) return;
                    token = matches[matches.Count - 1].Groups[1].Value;
                }

                var response = await PostAsync(
                    $"https://www.youtube.com/live_chat?key=live_chat%3D{videoId}&client_version=1.20240101.00.00",
                    JsonSerializer.Serialize(new Dictionary<string, string> { ["continuation"] = token }));

                foreach (var (author, text, color) in ParseMessages(response))
                    BridgeHost.Current?.EmitChat(ChatMsg.CreateText("youtube", _channel, author, text, color));

                var next = ExtractContinuation(response);
                if (next == null) return;
                token = next;
                await Task.Delay(10_000);
            }
        }
        catch
        {
            // токен протух или сбой сети — внешний цикл перезайдёт на страницу
        }
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
        foreach (var obj in FindByKey(root, "liveChatMessageRenderer"))
        {
            var text = GetText(obj, "message", "body");
            if (string.IsNullOrWhiteSpace(text)) continue;
            var author = GetText(obj, "author", "displayName") ?? "YouTube";
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
            if (obj.TryGetPropertyValue(key, out _)) yield return obj;
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

    private static string? GetText(JsonObject o, string first, string second)
    {
        if (o[first] is not JsonObject a) return null;
        if (a[second] is not JsonObject b) return null;
        if (b["simpleText"] is JsonValue st && st.GetValueKind() == JsonValueKind.String)
            return st.GetValue<string>();
        if (b["runs"] is JsonArray runs && runs.Count > 0 &&
            runs[0] is JsonObject r0 && r0["text"] is JsonValue tv && tv.GetValueKind() == JsonValueKind.String)
            return tv.GetValue<string>();
        return null;
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace YawaChatHub.Services;

/// <summary>
/// Управляет подключениями к площадкам (ТЗ §6). Критичный модуль: обход
/// корпоративных прокси через ServerCertificateCustomValidationCallback (в Program.cs).
/// </summary>
public sealed class ConnectorManager
{
    private static readonly Lazy<ConnectorManager> _lazy = new(() => new ConnectorManager());
    public static ConnectorManager Instance => _lazy.Value;

    private readonly object _lock = new();
    private readonly Dictionary<string, IConnector> _connectors = new();
    private readonly List<Channel> _channels = new();

    private ConnectorManager() { }

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
        StartConnector(platform, channelId);
    }

    public void Remove(string platform, string channelId)
    {
        lock (_lock)
            _channels.RemoveAll(c => c.Platform == platform && c.ChannelId == channelId);
    }

    public void Diagnose()
    {
        foreach (var ch in List())
            BridgeHost.Current?.EmitChat(ChatMsg.Text(ch.Platform, ch.ChannelId, "system", $"диагностика: канал {ch.ChannelId} активен"));
    }

    private void StartConnector(string platform, string channelId)
    {
        IConnector c = platform switch
        {
            "twitch" => new IrcConnector("irc.chat.twitch.tv", 6697, "justinfan" + new Random().Next(10000, 99999), platform, channelId),
            "kick" => new IrcConnector("irc.kick.com", 6697, "yawachat_" + new Random().Next(1000, 9999), platform, channelId),
            _ => new StubConnector(platform, channelId),
        };
        _connectors[$"{platform}:{channelId}"] = c;
        _ = Task.Run(() => c.Run());
    }
}

public interface IConnector { Task Run(); }

/// <summary>Заглушка для YouTube/VK/TikTok — сюда подключается реальная логика.</summary>
public sealed class StubConnector : IConnector
{
    private readonly string _platform, _channel;
    public StubConnector(string platform, string channel) { _platform = platform; _channel = channel; }

    public async Task Run()
    {
        // Точка расширения: YouTube Live polling (без API-ключа), VK Video Live,
        // TikTok Live (WebSocket). Здесь не блокируем основной поток.
        await Task.Delay(500);
        BridgeHost.Current?.EmitChat(ChatMsg.Text(_platform, _channel, "system",
            $"Подключение {_platform} ({_channel}) ожидает реализации коннектора"));
    }
}

/// <summary>IRC-коннектор для Twitch и Kick (анонимное чтение публичного чата).</summary>
public sealed class IrcConnector : IConnector
{
    private static readonly Regex PrivMsg =
        new(@"^(?:@(?<tags>[^ ]+) )?:(?<nick>[^!]+)![^ ]+ PRIVMSG #(?<chan>\S+) :(?<text>.*)$",
            RegexOptions.Compiled);

    private readonly string _host; private readonly int _port;
    private readonly string _nick; private readonly string _platform; private readonly string _channel;

    public IrcConnector(string host, int port, string nick, string platform, string channel)
    {
        _host = host; _port = port; _nick = nick; _platform = platform; _channel = channel.TrimStart('#').TrimStart('@');
    }

    public async Task Run()
    {
        var delay = 2000;
        while (true)
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(_host, _port);
                using var ssl = new SslStream(tcp.GetStream(), false,
                    (_, _, _, _) => true); // корпоративный прокси с TLS-инспекцией
                await ssl.AuthenticateAsClientAsync(_host);

                await Write(ssl, $"NICK {_nick}\r\n");
                await Write(ssl, "CAP REQ :twitch.tv/tags twitch.tv/commands twitch.tv/membership\r\n");
                await Write(ssl, $"JOIN #{_channel}\r\n");

                var reader = new StreamReader(ssl, Encoding.UTF8);
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (line.StartsWith("PING "))
                    { await Write(ssl, "PONG " + line[5..] + "\r\n"); continue; }

                    var m = PrivMsg.Match(line);
                    if (!m.Success) continue;
                    var author = m.Groups["nick"].Value;
                    var text = m.Groups["text"].Value;
                    if (author.Equals(_nick, StringComparison.OrdinalIgnoreCase)) continue;
                    BridgeHost.Current?.EmitChat(ChatMsg.Text(_platform, _channel, author, text));
                }
            }
            catch { /* сеть упала — переподключение */ }
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

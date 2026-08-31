using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace YawaChatHub.Services;

/// <summary>
/// YouTube Live Chat БЕЗ Data API и без ключа разработчика (ТЗ §19.4).
/// Порт рабочей реализации youtube.js: страница /live → ytInitialData →
/// continuation → POST youtubei/v1/live_chat/get_live_chat.
///
/// INNERTUBE_KEY ниже не является секретом: он зашит в каждую страницу
/// youtube.com для клиента WEB и используется самим сайтом.
/// </summary>
public static class YoutubeChat
{
    private const string InnertubeKey = "AIzaSyAO_FJ2SlqU8Q4STEHLGCilw_Y9_11qcW8";
    private const string ClientVersion = "2.20241216.01.00";

    public sealed class ChatSession
    {
        public string Key { get; init; } = InnertubeKey;
        public string ClientVersion { get; init; } = YoutubeChat.ClientVersion;
        public string Continuation { get; set; } = "";
    }

    public sealed class ChatItem
    {
        public string Author { get; init; } = "";
        public string Text { get; init; } = "";
        public List<string> Badges { get; init; } = new();
    }

    /// <summary>Находит id идущего эфира канала (@handle) или пустую строку.</summary>
    public static async Task<string> FindLiveVideoIdAsync(string handle, CancellationToken ct)
    {
        var h = handle.Trim();
        if (!h.StartsWith("@")) h = "@" + h;

        foreach (var url in new[]
        {
            $"https://www.youtube.com/{h}/live",
            $"https://www.youtube.com/{h}/streams",
            $"https://www.youtube.com/{h}",
        })
        {
            var html = await Net.GetTextAsync(url, null, ct);
            if (string.IsNullOrEmpty(html)) continue;

            // Канонический live-URL — самый надёжный признак идущего эфира.
            var canonical = Regex.Match(html, @"<link rel=""canonical"" href=""https://www\.youtube\.com/watch\?v=([\w-]{11})""");
            if (canonical.Success && html.Contains("\"isLive\":true"))
                return canonical.Groups[1].Value;

            var data = FindJson(html, "ytInitialData");
            if (data == null) continue;

            foreach (var node in Walk(data))
            {
                if (node["videoId"] is not JsonValue idv) continue;
                var id = idv.ToString();
                if (id.Length != 11) continue;
                var blob = node.ToJsonString();
                if (blob.Contains("\"isLiveNow\":true") ||
                    blob.Contains("BADGE_STYLE_TYPE_LIVE_NOW") ||
                    blob.Contains("\"isLive\":true"))
                    return id;
            }
        }
        return "";
    }

    /// <summary>Открывает live-чат: достаёт ключ, версию клиента и continuation.</summary>
    public static async Task<ChatSession?> OpenChatAsync(string videoId, CancellationToken ct)
    {
        var html = await Net.GetTextAsync(
            $"https://www.youtube.com/live_chat?is_popout=1&v={videoId}",
            new[] { ("Referer", $"https://www.youtube.com/watch?v={videoId}") }, ct);
        if (string.IsNullOrEmpty(html)) return null;

        var key = Pick(html, "\"INNERTUBE_API_KEY\":\"([^\"]+)\"");
        var version = Pick(html, "\"INNERTUBE_CLIENT_VERSION\":\"([^\"]+)\"");

        var continuation = "";
        var data = FindJson(html, "ytInitialData");
        if (data != null) continuation = ContinuationFrom(data)?.Token ?? "";
        if (string.IsNullOrEmpty(continuation))
            continuation = Pick(html, "\"continuation\":\"([^\"]{20,})\"");
        if (string.IsNullOrEmpty(continuation)) return null;

        return new ChatSession
        {
            Key = string.IsNullOrEmpty(key) ? InnertubeKey : key,
            ClientVersion = string.IsNullOrEmpty(version) ? ClientVersion : version,
            Continuation = continuation,
        };
    }

    /// <summary>Один шаг polling: новые сообщения + следующий continuation.</summary>
    public static async Task<(List<ChatItem> Messages, int TimeoutMs)?> PollAsync(ChatSession s, CancellationToken ct)
    {
        var payload = new
        {
            context = new
            {
                client = new
                {
                    clientName = "WEB",
                    clientVersion = s.ClientVersion,
                    hl = "ru",
                    gl = "RU",
                },
            },
            continuation = s.Continuation,
        };

        var data = await Net.PostJsonAsync(
            $"https://www.youtube.com/youtubei/v1/live_chat/get_live_chat?key={s.Key}&prettyPrint=false",
            payload,
            new[]
            {
                ("Origin", "https://www.youtube.com"),
                ("Referer", "https://www.youtube.com/"),
                ("X-YouTube-Client-Name", "1"),
                ("X-YouTube-Client-Version", s.ClientVersion),
            }, ct);

        var lc = data?["continuationContents"]?["liveChatContinuation"];
        if (lc == null) return null;

        var messages = ParseActions(lc["actions"] as JsonArray);
        var next = ContinuationFrom(lc);
        if (next != null && !string.IsNullOrEmpty(next.Token)) s.Continuation = next.Token;
        return (messages, next?.TimeoutMs ?? 4000);
    }

    // ── разбор ──────────────────────────────────────────────────────────────

    private sealed record Cont(string Token, int TimeoutMs);

    private static Cont? ContinuationFrom(JsonNode root)
    {
        foreach (var node in Walk(root))
        {
            var c =
                node["invalidationContinuationData"] ??
                node["timedContinuationData"] ??
                node["reloadContinuationData"] ??
                node["liveChatReplayContinuationData"];
            var token = c?["continuation"]?.ToString();
            if (!string.IsNullOrEmpty(token) && token.Length > 20)
            {
                var t = c?["timeoutMs"];
                var ms = t != null && int.TryParse(t.ToString(), out var v) ? v : 4000;
                return new Cont(token, ms);
            }
        }
        return null;
    }

    private static List<ChatItem> ParseActions(JsonArray? actions)
    {
        var result = new List<ChatItem>();
        if (actions == null) return result;

        foreach (var a in actions)
        {
            var item = a?["addChatItemAction"]?["item"]
                    ?? a?["replayChatItemAction"]?["actions"]?[0]?["addChatItemAction"]?["item"];
            if (item == null) continue;

            var r = item["liveChatTextMessageRenderer"] ?? item["liveChatPaidMessageRenderer"];
            if (r == null) continue;

            var author = r["authorName"]?["simpleText"]?.ToString() ?? "зритель";
            var text = RunsToText(r["message"]);
            if (string.IsNullOrWhiteSpace(text))
                text = RunsToText(r["headerSubtext"]);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var badges = new List<string>();
            if (r["authorBadges"] is JsonArray ab)
            {
                foreach (var b in ab)
                {
                    var tip = b?["liveChatAuthorBadgeRenderer"]?["tooltip"]?.ToString() ?? "";
                    if (Regex.IsMatch(tip, "moderator|модератор", RegexOptions.IgnoreCase)) badges.Add("mod");
                    else if (Regex.IsMatch(tip, "member|спонсор|участник", RegexOptions.IgnoreCase)) badges.Add("member");
                    else if (Regex.IsMatch(tip, "verified|owner|влад", RegexOptions.IgnoreCase)) badges.Add("vip");
                }
            }
            if (item["liveChatPaidMessageRenderer"] != null) badges.Add("fan");

            result.Add(new ChatItem { Author = author, Text = text, Badges = badges });
        }
        return result;
    }

    private static string RunsToText(JsonNode? message)
    {
        if (message?["simpleText"] is JsonValue simple) return simple.ToString();
        if (message?["runs"] is not JsonArray runs) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var run in runs)
        {
            if (run?["text"] is JsonValue t) sb.Append(t.ToString());
            else
            {
                var label = run?["emoji"]?["shortcuts"]?[0]?.ToString()
                         ?? run?["emoji"]?["emojiId"]?.ToString();
                if (!string.IsNullOrEmpty(label)) sb.Append(' ').Append(label).Append(' ');
            }
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    /// <summary>Достаёт JSON-объект, следующий за маркером (ytInitialData).</summary>
    private static JsonNode? FindJson(string html, string marker)
    {
        var at = html.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return null;
        var start = html.IndexOf('{', at);
        if (start < 0) return null;

        var depth = 0; var inStr = false; var esc = false;
        for (var i = start; i < html.Length; i++)
        {
            var ch = html[i];
            if (inStr)
            {
                if (esc) esc = false;
                else if (ch == '\\') esc = true;
                else if (ch == '"') inStr = false;
                continue;
            }
            if (ch == '"') { inStr = true; continue; }
            if (ch == '{') depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    try { return JsonNode.Parse(html[start..(i + 1)]); }
                    catch { return null; }
                }
            }
        }
        return null;
    }

    private static string Pick(string text, string pattern)
    {
        var m = Regex.Match(text, pattern);
        return m.Success ? m.Groups[1].Value : "";
    }

    private static IEnumerable<JsonObject> Walk(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            yield return obj;
            foreach (var child in obj.Select(p => p.Value))
                foreach (var n in Walk(child)) yield return n;
        }
        else if (node is JsonArray arr)
        {
            foreach (var child in arr)
                foreach (var n in Walk(child)) yield return n;
        }
    }
}

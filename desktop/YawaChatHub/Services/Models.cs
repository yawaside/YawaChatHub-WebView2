using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace YawaChatHub.Services;

public class MsgPart
{
    [JsonPropertyName("t")] public string T { get; set; } = "text";
    [JsonPropertyName("v")] public string V { get; set; } = "";
    [JsonPropertyName("url")] public string? Url { get; set; }
}

public class ChatMsg
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("platform")] public string Platform { get; set; } = "";
    [JsonPropertyName("channelId")] public string ChannelId { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("color")] public string Color { get; set; } = "#8b7bff";
    [JsonPropertyName("badges")] public List<string> Badges { get; set; } = new();
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("parts")] public List<MsgPart> Parts { get; set; } = new();
    [JsonPropertyName("ts")] public long Ts { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    [JsonPropertyName("sys")] public bool Sys { get; set; }
    [JsonPropertyName("kind")] public string? Kind { get; set; }

    public static ChatMsg CreateText(string platform, string channel, string author, string text)
    {
        var m = new ChatMsg
        {
            Platform = platform, ChannelId = channel, Author = author, Text = text,
            Color = NameColor(author),
        };
        m.Parts.Add(new MsgPart { T = "text", V = text });
        return m;
    }

    private static string NameColor(string name)
    {
        int h = 0;
        foreach (var c in name) h = h * 31 + c;
        var hue = ((h % 360) + 360) % 360;
        return $"hsl({hue} 85% 68%)";
    }
}

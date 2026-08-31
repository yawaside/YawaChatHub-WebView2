using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace YawaChatHub.Services;

/// <summary>
/// HTTP + WebSocket-сервер для OBS (ТЗ §12). Статичная ссылка:
/// http://127.0.0.1:{port}/widget?token={token}. Оформление — только по WebSocket.
/// </summary>
public sealed class WidgetServer
{
    private static readonly Lazy<WidgetServer> _lazy = new(() => new WidgetServer());
    public static WidgetServer Instance => _lazy.Value;

    private readonly object _lock = new();
    private readonly List<WebSocket> _clients = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _port;

    private WidgetServer() { }

    public string Url => $"http://127.0.0.1:{_port}/widget?token={SettingsService.Instance.Current.Token}";
    public object Info => new
    {
        port = _port,
        token = SettingsService.Instance.Current.Token,
        url = Url,
    };

    public void Start(int preferredPort)
    {
        _port = preferredPort;
        while (true)
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                _listener.Start();
                break;
            }
            catch
            {
                _port++; // автоинкремент при конфликте (ТЗ §20)
                if (_port > preferredPort + 50) return;
            }
        }
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => Loop(_cts.Token));
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ctx = await _listener!.GetContextAsync();
                _ = Task.Run(() => Handle(ctx), ct);
            }
            catch { break; }
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";
        if (path.EndsWith("/widget"))
        {
            var token = ctx.Request.QueryString["token"] ?? "";
            if (token != SettingsService.Instance.Current.Token)
            {
                ctx.Response.StatusCode = 401;
                ctx.Response.Close();
                return;
            }
            if (ctx.Request.IsWebSocketRequest)
            {
                var ws = await ctx.AcceptWebSocketAsync(null);
                OnClient(ws.WebSocket);
                return;
            }
            // HTML-виджет встроен в portable EXE и извлечён WebAssets.
            WebAssets.EnsureExtracted();
            await ServeFile(ctx, WebAssets.WidgetHtmlPath);
            return;
        }
        ctx.Response.StatusCode = 404;
        ctx.Response.Close();
    }

    private static async Task ServeFile(HttpListenerContext ctx, string file)
    {
        if (!File.Exists(file))
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }
        var bytes = await File.ReadAllBytesAsync(file);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private void OnClient(WebSocket ws)
    {
        lock (_lock) _clients.Add(ws);
        BridgeHost.Current?.EmitWidgetClients(_clients.Count);

        // при подключении нового клиента — сразу текущий config (ТЗ §12.2)
        SendConfig(SettingsService.Instance.ToJson());

        _ = Task.Run(async () =>
        {
            var buf = new byte[4096];
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var r = await ws.ReceiveAsync(buf, CancellationToken.None);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                }
            }
            catch { }
            lock (_lock) _clients.Remove(ws);
            BridgeHost.Current?.EmitWidgetClients(_clients.Count);
        });
    }

    public void Broadcast(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        lock (_lock)
        {
            foreach (var c in _clients)
            {
                if (c.State != WebSocketState.Open) continue;
                _ = Task.Run(async () =>
                {
                    try { await c.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
                    catch { }
                });
            }
        }
    }

    public void SendConfig(string cfgJson)
    {
        try
        {
            var cfg = JsonSerializer.Deserialize<Dictionary<string, object>>(cfgJson);
            var widget = cfg != null && cfg.TryGetValue("widget", out var w) ? w : new Dictionary<string, object>();
            Broadcast(new { type = "config", cfg = widget, look = widget });
        }
        catch { }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
    }
}

using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StreamingHost.Signaling;

/// <summary>
/// HTTP/WS server hosted in-process so the PC client is self-contained — no
/// external relay needed. The mobile viewer connects directly to
/// ws://&lt;PC public IP&gt;:&lt;port&gt;/ws and we mediate the WebRTC handshake here.
///
/// One host process serves one room. Room code is for pairing/auth only;
/// the host doesn't multiplex multiple games.
/// </summary>
public sealed class EmbeddedSignalingServer : IAsyncDisposable
{
    public string RoomCode { get; }
    public int Port { get; }

    private WebApplication? _app;
    private readonly ConcurrentDictionary<string, ViewerSession> _viewers = new();

    /// <summary>Fired when a viewer joins (after room-code validation). Subscribers create the WebRTC peer.</summary>
    public event Action<ViewerSession>? ViewerJoined;
    public event Action<string>? ViewerLeft;     // viewerId
    public event Action<string>? ServerError;

    public EmbeddedSignalingServer(string roomCode, int port = 8080)
    {
        RoomCode = roomCode;
        Port = port;
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
        builder.WebHost.UseUrls($"http://0.0.0.0:{Port}");

        _app = builder.Build();
        _app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(20),
        });

        _app.MapGet("/health", () => Results.Text("ok"));
        _app.MapGet("/ws", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            await HandleViewerSocket(ws);
        });

        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }

    public ViewerSession? GetViewer(string viewerId) =>
        _viewers.TryGetValue(viewerId, out var v) ? v : null;

    private async Task HandleViewerSocket(WebSocket ws)
    {
        ViewerSession? session = null;
        var buffer = new byte[64 * 1024];

        try
        {
            while (ws.State == WebSocketState.Open)
            {
                using var ms = new System.IO.MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var text = Encoding.UTF8.GetString(ms.ToArray());
                var msg = JsonNode.Parse(text);
                if (msg is null) continue;

                var type = msg["type"]?.GetValue<string>();

                if (type == "join")
                {
                    var room = msg["room"]?.GetValue<string>();
                    if (room != RoomCode)
                    {
                        await SendAsync(ws, new { type = "error", code = "bad-room" });
                        continue;
                    }
                    session = new ViewerSession(ws, this);
                    _viewers[session.ViewerId] = session;
                    await SendAsync(ws, new { type = "joined", viewerId = session.ViewerId, hostReady = true });
                    ViewerJoined?.Invoke(session);
                    continue;
                }

                if (session is null)
                {
                    await SendAsync(ws, new { type = "error", code = "not-joined" });
                    continue;
                }

                // Forward "answer" / "ice" to host (via session event)
                session.RaiseMessage(type ?? "", msg);
            }
        }
        catch (Exception ex)
        {
            ServerError?.Invoke(ex.Message);
        }
        finally
        {
            if (session is not null)
            {
                _viewers.TryRemove(session.ViewerId, out _);
                ViewerLeft?.Invoke(session.ViewerId);
            }
        }
    }

    internal static async Task SendAsync(WebSocket ws, object payload)
    {
        if (ws.State != WebSocketState.Open) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }
}

/// <summary>One viewer's signaling channel. Owned by the server; consumed by the WebRTC peer.</summary>
public sealed class ViewerSession
{
    public string ViewerId { get; } = "v_" + Guid.NewGuid().ToString("N")[..8];
    private readonly WebSocket _ws;
    private readonly EmbeddedSignalingServer _server;

    /// <summary>type ("answer"|"ice"), full JSON node.</summary>
    public event Action<string, JsonNode>? Message;

    internal ViewerSession(WebSocket ws, EmbeddedSignalingServer server)
    {
        _ws = ws;
        _server = server;
    }

    internal void RaiseMessage(string type, JsonNode msg) => Message?.Invoke(type, msg);

    public Task SendOfferAsync(string sdp) => EmbeddedSignalingServer.SendAsync(_ws, new { type = "offer", sdp });
    public Task SendIceAsync(object candidate) => EmbeddedSignalingServer.SendAsync(_ws, new { type = "ice", candidate });
}

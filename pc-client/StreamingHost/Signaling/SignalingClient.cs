using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace StreamingHost.Signaling;

/// <summary>
/// WebSocket signaling client for relaying SDP/ICE between this host and remote viewers.
/// </summary>
public sealed class SignalingClient : IAsyncDisposable
{
    private readonly Uri _endpoint;
    private readonly string _roomCode;
    private readonly ClientWebSocket _ws = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveLoop;

    public event Action<string>? ViewerJoined;          // viewerId
    public event Action<string>? ViewerLeft;            // viewerId
    public event Action<string, JsonNode>? OfferAnswer; // type ("offer"|"answer"), full message
    public event Action<string, JsonNode>? IceReceived; // viewerId, candidate
    public event Action<string>? Error;

    public SignalingClient(Uri endpoint, string roomCode)
    {
        _endpoint = endpoint;
        _roomCode = roomCode;
    }

    public async Task ConnectAndRegisterAsync()
    {
        await _ws.ConnectAsync(_endpoint, _cts.Token);
        await SendAsync(new
        {
            type = "register",
            role = "host",
            room = _roomCode
        });
        _receiveLoop = Task.Run(ReceiveLoopAsync);
    }

    public Task SendOfferAsync(string viewerId, string sdp) =>
        SendAsync(new { type = "offer", to = viewerId, sdp });

    public Task SendAnswerAsync(string viewerId, string sdp) =>
        SendAsync(new { type = "answer", to = viewerId, sdp });

    public Task SendIceAsync(string viewerId, object candidate) =>
        SendAsync(new { type = "ice", to = viewerId, candidate });

    private async Task SendAsync(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token);
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (!_cts.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                using var ms = new System.IO.MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer, _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var text = Encoding.UTF8.GetString(ms.ToArray());
                HandleMessage(text);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Error?.Invoke(ex.Message);
        }
    }

    private void HandleMessage(string text)
    {
        var node = JsonNode.Parse(text);
        if (node is null) return;
        var type = node["type"]?.GetValue<string>();
        switch (type)
        {
            case "registered":
                // ack — nothing else to do
                break;

            case "viewer-joined":
                {
                    var viewerId = node["viewerId"]?.GetValue<string>();
                    if (viewerId is not null) ViewerJoined?.Invoke(viewerId);
                    break;
                }

            case "viewer-left":
                {
                    var viewerId = node["viewerId"]?.GetValue<string>();
                    if (viewerId is not null) ViewerLeft?.Invoke(viewerId);
                    break;
                }

            case "answer":
            case "offer":
                OfferAnswer?.Invoke(type, node);
                break;

            case "ice":
                {
                    var from = node["from"]?.GetValue<string>();
                    var cand = node["candidate"];
                    if (from is not null && cand is not null) IceReceived?.Invoke(from, cand);
                    break;
                }

            case "error":
                Error?.Invoke(node["code"]?.GetValue<string>() ?? "unknown");
                break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            if (_ws.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        catch { }
        if (_receiveLoop is not null) await _receiveLoop;
        _ws.Dispose();
    }
}

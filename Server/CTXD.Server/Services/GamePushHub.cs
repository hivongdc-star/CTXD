using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CTXD.Server.Services;

public sealed class GamePushHub
{
    readonly ConcurrentDictionary<long, ConcurrentDictionary<Guid, WebSocket>> _players = new();
    readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task HoldAsync(long playerId, WebSocket socket, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var group = _players.GetOrAdd(playerId, _ => new ConcurrentDictionary<Guid, WebSocket>());
        group[id] = socket;
        try
        {
            await SendSocketAsync(socket, new { type = "connected", payload = new { playerId } }, ct);
            var buffer = new byte[1024];
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                // Client messages are intentionally ignored in first playable. Gameplay remains HTTP/server-authoritative.
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (WebSocketException) { }
        finally
        {
            group.TryRemove(id, out _);
            if (group.IsEmpty) _players.TryRemove(playerId, out _);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None); }
                catch { }
            }
            socket.Dispose();
        }
    }

    public async Task SendAsync(long playerId, string type, object payload, CancellationToken ct = default)
    {
        if (!_players.TryGetValue(playerId, out var group)) return;
        var message = new { type, payload };
        foreach (var pair in group.ToArray())
        {
            var socket = pair.Value;
            if (socket.State != WebSocketState.Open)
            {
                group.TryRemove(pair.Key, out _);
                continue;
            }
            try { await SendSocketAsync(socket, message, ct); }
            catch
            {
                group.TryRemove(pair.Key, out _);
                try { socket.Abort(); } catch { }
            }
        }
    }

    public async Task BroadcastAsync(string type, object payload, CancellationToken ct = default)
    {
        foreach (var playerId in _players.Keys.ToArray())
            await SendAsync(playerId, type, payload, ct);
    }

    async Task SendSocketAsync(WebSocket socket, object message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, _json));
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }
}

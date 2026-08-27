using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed class GamePushHub(GameDb db)
{
    sealed record Connection(WebSocket Socket,short ForceId,int PlayerLevel);

    readonly ConcurrentDictionary<long, ConcurrentDictionary<Guid, Connection>> _players = new();
    readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task HoldAsync(long playerId, WebSocket socket, CancellationToken ct)
    {
        var membership=await ReadMembershipAsync(playerId,ct);
        var id = Guid.NewGuid();
        var group = _players.GetOrAdd(playerId, _ => new ConcurrentDictionary<Guid, Connection>());
        group[id] = new Connection(socket,membership.ForceId,membership.Level);
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
            await SendConnectionAsync(group,pair.Key,pair.Value,message,ct);
    }

    public async Task SendCountryGroupAsync(short forceId,int? subgroup,string type,object payload,CancellationToken ct=default)
    {
        var message=new{type,payload};
        foreach(var player in _players.ToArray())
        {
            var group=player.Value;
            foreach(var pair in group.ToArray())
            {
                var connection=pair.Value;
                if(connection.ForceId!=forceId||!InCountrySubgroup(connection.PlayerLevel,subgroup))continue;
                await SendConnectionAsync(group,pair.Key,connection,message,ct);
            }
        }
    }

    public async Task BroadcastAsync(string type, object payload, CancellationToken ct = default)
    {
        foreach (var playerId in _players.Keys.ToArray())
            await SendAsync(playerId, type, payload, ct);
    }

    async Task<(short ForceId,int Level)> ReadMembershipAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var q=new NpgsqlCommand("SELECT force_id,level FROM players WHERE id=$1",c);
        q.Parameters.AddWithValue(playerId);
        await using var r=await q.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);
        return(r.GetInt16(0),r.GetInt32(1));
    }

    static bool InCountrySubgroup(int playerLevel,int? subgroup)=>subgroup switch
    {
        null=>true,
        1=>playerLevel<=30,
        2=>playerLevel is>=31 and<=50,
        _=>false
    };

    async Task SendConnectionAsync(ConcurrentDictionary<Guid,Connection> group,Guid id,Connection connection,object message,CancellationToken ct)
    {
        var socket=connection.Socket;
        if (socket.State != WebSocketState.Open)
        {
            group.TryRemove(id, out _);
            return;
        }
        try { await SendSocketAsync(socket, message, ct); }
        catch
        {
            group.TryRemove(id, out _);
            try { socket.Abort(); } catch { }
        }
    }

    async Task SendSocketAsync(WebSocket socket, object message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, _json));
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }
}

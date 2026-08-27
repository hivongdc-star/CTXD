using System.Globalization;
using CTXD.Server.Data;
using CTXD.Server.Domain;
using Npgsql;

namespace CTXD.Server.Services;

public static class WorldTreasureBoxState
{
    public static async Task EnsureAsync(NpgsqlConnection c,NpgsqlTransaction? t,CanonicalContent content,long playerId,int force,CancellationToken ct)
    {
        if(force is <1 or >3)return;
        await using(var exists=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM player_world_treasure_boxes WHERE player_id=$1)",c,t))
        {
            exists.Parameters.AddWithValue(playerId);
            if(Convert.ToBoolean(await exists.ExecuteScalarAsync(ct)))return;
        }
        var boxes=content.WorldRoads.Values.OrderBy(x=>x.Id).Select(x=>(roadId:x.Id,treasureId:TreasureId(x,force))).Where(x=>x.treasureId>0).ToArray();
        if(boxes.Length==0)return;
        await using var cmd=new NpgsqlCommand(@"INSERT INTO player_world_treasure_boxes(player_id,road_id,treasure_id)
SELECT $1,x.road_id,x.treasure_id
FROM unnest($2::integer[],$3::integer[]) AS x(road_id,treasure_id)
ON CONFLICT(player_id,road_id) DO NOTHING",c,t);
        cmd.Parameters.AddWithValue(playerId);
        cmd.Parameters.AddWithValue(boxes.Select(x=>x.roadId).ToArray());
        cmd.Parameters.AddWithValue(boxes.Select(x=>x.treasureId).ToArray());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task<int?> PickAsync(NpgsqlConnection c,NpgsqlTransaction? t,CanonicalContent content,long playerId,int force,int roadId,CancellationToken ct)
    {
        if(force is <1 or >3)return null;
        await EnsureAsync(c,t,content,playerId,force,ct);
        await using var cmd=new NpgsqlCommand(@"UPDATE player_world_treasure_boxes
SET picked_at=COALESCE(picked_at,now())
WHERE player_id=$1 AND road_id=$2 AND picked_at IS NULL
RETURNING treasure_id",c,t);
        cmd.Parameters.AddWithValue(playerId);cmd.Parameters.AddWithValue(roadId);
        var value=await cmd.ExecuteScalarAsync(ct);
        return value is null or DBNull?null:Convert.ToInt32(value);
    }

    public static async Task<bool> HasGottenAllAsync(NpgsqlConnection c,NpgsqlTransaction? t,CanonicalContent content,long playerId,CancellationToken ct)
    {
        int force;
        await using(var state=new NpgsqlCommand(@"SELECT p.force_id
FROM players p
JOIN player_world pw ON pw.player_id=p.id
WHERE p.id=$1",c,t))
        {
            state.Parameters.AddWithValue(playerId);
            var value=await state.ExecuteScalarAsync(ct);
            if(value is null or DBNull)return false;
            force=Convert.ToInt32(value);
        }
        if(force is <1 or >3)return false;
        await EnsureAsync(c,t,content,playerId,force,ct);
        await using var cmd=new NpgsqlCommand("SELECT NOT EXISTS(SELECT 1 FROM player_world_treasure_boxes WHERE player_id=$1 AND picked_at IS NULL)",c,t);
        cmd.Parameters.AddWithValue(playerId);
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct));
    }

    static int TreasureId(WorldRoadDefinition road,int force)
    {
        var raw=force switch{1=>road.WeiReward,2=>road.ShuReward,3=>road.WuReward,_=>""};
        return string.IsNullOrWhiteSpace(raw)?0:int.Parse(raw,CultureInfo.InvariantCulture);
    }
}

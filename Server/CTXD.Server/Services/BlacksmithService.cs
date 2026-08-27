using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record BlacksmithSmithView(
    int SmithId,
    bool Unlocked,
    int Level,
    int DailyUsed,
    int DailyLimit,
    int BlueprintItemId,
    int BlueprintItemType,
    int BlueprintCount,
    int StoneItemId,
    int StoneItemType,
    int StoneCount,
    int IronPerDissolve);

public sealed record BlacksmithView(bool FunctionOpen,int PlayerLevel,long Iron,BlacksmithSmithView Smith1);
public sealed record BlacksmithUnlockResult(int SmithId,int Level,int BlueprintItemId,int BlueprintItemType,int BlueprintConsumed);
public sealed record BlacksmithDissolveResult(int SmithId,int IronAdded,long Iron,int DailyUsed,int DailyLimit);

public sealed class BlacksmithService(GameDb db,IPlayerItemInventory items)
{
    const int FunctionId=66;
    const int SmithId=1;
    const int UnlockLevel=100;
    const int BlueprintItemId=1201;
    const int BlueprintItemType=15;
    const int StoneItemId=1401;
    const int StoneItemType=16;
    const int DailyDissolveLimit=5;
    const int IronPerDissolve=4000;

    public async Task<BlacksmithView> GetAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var q=new NpgsqlCommand(@"
SELECT p.level,
       EXISTS(SELECT 1 FROM player_functions f WHERE f.player_id=p.id AND f.function_id=$2),
       COALESCE(b.level,0),
       CASE WHEN b.usage_day=CURRENT_DATE THEN COALESCE(b.daily_dissolve_usage,0) ELSE 0 END,
       COALESCE((SELECT quantity FROM player_items WHERE player_id=p.id AND item_id=$3 AND item_type=$4),0),
       COALESCE((SELECT quantity FROM player_items WHERE player_id=p.id AND item_id=$5 AND item_type=$6),0),
       COALESCE(r.iron,0)
FROM players p
LEFT JOIN player_blacksmiths b ON b.player_id=p.id AND b.smith_id=$7
LEFT JOIN player_resources r ON r.player_id=p.id
WHERE p.id=$1",c);
        q.Parameters.AddWithValue(playerId);
        q.Parameters.AddWithValue(FunctionId);
        q.Parameters.AddWithValue(BlueprintItemId);
        q.Parameters.AddWithValue(BlueprintItemType);
        q.Parameters.AddWithValue(StoneItemId);
        q.Parameters.AddWithValue(StoneItemType);
        q.Parameters.AddWithValue(SmithId);
        await using var r=await q.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);
        var playerLevel=r.GetInt32(0);
        var functionOpen=r.GetBoolean(1);
        var smithLevel=r.GetInt32(2);
        var dailyUsed=r.GetInt32(3);
        var blueprintCount=r.GetInt32(4);
        var stoneCount=r.GetInt32(5);
        var iron=r.GetInt64(6);
        return new(functionOpen,playerLevel,iron,new(
            SmithId,smithLevel>0,smithLevel,dailyUsed,DailyDissolveLimit,
            BlueprintItemId,BlueprintItemType,blueprintCount,
            StoneItemId,StoneItemType,stoneCount,IronPerDissolve));
    }

    public async Task<BlacksmithUnlockResult> UnlockSmith1Async(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);

        int playerLevel;
        await using(var player=new NpgsqlCommand("SELECT level FROM players WHERE id=$1 FOR UPDATE",c,t))
        {
            player.Parameters.AddWithValue(playerId);
            var value=await player.ExecuteScalarAsync(ct);
            if(value is null)throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);
            playerLevel=Convert.ToInt32(value);
        }

        await using(var gate=new NpgsqlCommand("SELECT 1 FROM player_functions WHERE player_id=$1 AND function_id=$2",c,t))
        {
            gate.Parameters.AddWithValue(playerId);
            gate.Parameters.AddWithValue(FunctionId);
            if(await gate.ExecuteScalarAsync(ct)is null)throw new GameException("BLACKSMITH_FUNCTION_LOCKED","Blacksmith function is not open.");
        }
        if(playerLevel<UnlockLevel)throw new GameException("BLACKSMITH_LEVEL_REQUIRED","Player level 100 is required to unlock smith 1.");

        await using(var existing=new NpgsqlCommand("SELECT 1 FROM player_blacksmiths WHERE player_id=$1 AND smith_id=$2",c,t))
        {
            existing.Parameters.AddWithValue(playerId);
            existing.Parameters.AddWithValue(SmithId);
            if(await existing.ExecuteScalarAsync(ct)is not null)throw new GameException("BLACKSMITH_ALREADY_UNLOCKED","Smith 1 is already unlocked.");
        }

        if(!await items.ConsumeAsync(c,t,playerId,BlueprintItemId,BlueprintItemType,1,ct))
            throw new GameException("BLACKSMITH_BLUEPRINT_REQUIRED","Smith 1 requires one level-1 blacksmith blueprint.");

        await using(var add=new NpgsqlCommand(@"
INSERT INTO player_blacksmiths(player_id,smith_id,level,daily_dissolve_usage,usage_day)
VALUES($1,$2,1,0,CURRENT_DATE)",c,t))
        {
            add.Parameters.AddWithValue(playerId);
            add.Parameters.AddWithValue(SmithId);
            await add.ExecuteNonQueryAsync(ct);
        }

        await t.CommitAsync(ct);
        return new(SmithId,1,BlueprintItemId,BlueprintItemType,1);
    }

    public async Task<BlacksmithDissolveResult> DissolveSmith1Async(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);

        await using(var reset=new NpgsqlCommand(@"
UPDATE player_blacksmiths
SET daily_dissolve_usage=0,usage_day=CURRENT_DATE,updated_at=now()
WHERE player_id=$1 AND smith_id=$2 AND usage_day<>CURRENT_DATE",c,t))
        {
            reset.Parameters.AddWithValue(playerId);
            reset.Parameters.AddWithValue(SmithId);
            await reset.ExecuteNonQueryAsync(ct);
        }

        int used;
        await using(var smith=new NpgsqlCommand("SELECT daily_dissolve_usage FROM player_blacksmiths WHERE player_id=$1 AND smith_id=$2 FOR UPDATE",c,t))
        {
            smith.Parameters.AddWithValue(playerId);
            smith.Parameters.AddWithValue(SmithId);
            var value=await smith.ExecuteScalarAsync(ct);
            if(value is null)throw new GameException("BLACKSMITH_SMITH_LOCKED","Smith 1 is not unlocked.");
            used=Convert.ToInt32(value);
        }
        if(used>=DailyDissolveLimit)throw new GameException("BLACKSMITH_DAILY_LIMIT","Smith 1 can dissolve at most 5 stones per day.");

        if(!await items.ConsumeAsync(c,t,playerId,StoneItemId,StoneItemType,1,ct))
            throw new GameException("BLACKSMITH_STONE_REQUIRED","One Huyền Thiết Thạch is required.");

        long iron;
        await using(var grant=new NpgsqlCommand("UPDATE player_resources SET iron=iron+$2,update_time=now() WHERE player_id=$1 RETURNING iron",c,t))
        {
            grant.Parameters.AddWithValue(playerId);
            grant.Parameters.AddWithValue(IronPerDissolve);
            var value=await grant.ExecuteScalarAsync(ct);
            if(value is null)throw new GameException("PLAYER_RESOURCE_MISSING","Player resource state does not exist.",409);
            iron=Convert.ToInt64(value);
        }

        await using(var save=new NpgsqlCommand("UPDATE player_blacksmiths SET daily_dissolve_usage=daily_dissolve_usage+1,updated_at=now() WHERE player_id=$1 AND smith_id=$2",c,t))
        {
            save.Parameters.AddWithValue(playerId);
            save.Parameters.AddWithValue(SmithId);
            await save.ExecuteNonQueryAsync(ct);
        }

        await t.CommitAsync(ct);
        return new(SmithId,IronPerDissolve,iron,used+1,DailyDissolveLimit);
    }
}

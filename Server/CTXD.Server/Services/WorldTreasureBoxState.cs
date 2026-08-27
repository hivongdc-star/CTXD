using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using CTXD.Server.Data;
using CTXD.Server.Domain;
using Npgsql;

namespace CTXD.Server.Services;

public static class WorldTreasureBoxState
{
    public const string RewardNotificationChannel="ctxd_world_treasure";
    const string MailTitle="世界移动中获得宝箱";
    static readonly ConcurrentDictionary<string,IReadOnlyDictionary<int,WorldTreasureDefinition>> DefinitionsByDirectory=new(StringComparer.OrdinalIgnoreCase);

    sealed class WorldTreasureDefinition
    {
        public WorldTreasureDefinition() { }
        public int Id{get;set;}
        public int Type{get;set;}
        public string Name{get;set;}="";
        public string Reward{get;set;}="";
        public string TaskIntroduction{get;set;}="";
    }

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

    public static async Task PickAndGrantAsync(NpgsqlConnection c,NpgsqlTransaction t,CanonicalContent content,long playerId,int force,int roadId,int generalId,CancellationToken ct)
    {
        if(force is <1 or >3)return;
        await EnsureAsync(c,t,content,playerId,force,ct);

        int treasureId;
        await using(var box=new NpgsqlCommand(@"SELECT treasure_id
FROM player_world_treasure_boxes
WHERE player_id=$1 AND road_id=$2 AND picked_at IS NULL
FOR UPDATE",c,t))
        {
            box.Parameters.AddWithValue(playerId);box.Parameters.AddWithValue(roadId);
            var boxValue=await box.ExecuteScalarAsync(ct);
            if(boxValue is null or DBNull)return;
            treasureId=Convert.ToInt32(boxValue);
        }

        var definitions=Definitions(content);
        if(!definitions.TryGetValue(treasureId,out var treasure))
            throw new GameException("WORLD_TREASURE_STATIC_MISSING",$"Legacy WorldTreasure {treasureId} is missing.",500);
        var (kind,value)=ParseReward(treasure.Reward);
        var rewardSummary=await GrantAsync(c,t,content,playerId,kind,value,ct);

        if(!content.Generals.TryGetValue(generalId,out var general))
            throw new GameException("WORLD_TREASURE_GENERAL_MISSING",$"General {generalId} is missing from canonical data.",500);
        var body=$"您的武将{general.Name}在世界地图遇到了一个宝箱，获得：{rewardSummary}";
        var sourceKey=$"world-treasure-box:{roadId}:{treasureId}";
        await using(var mail=new NpgsqlCommand(@"INSERT INTO player_mail(recipient_player_id,title,body,mail_type,source_key)
VALUES($1,$2,$3,1,$4)
ON CONFLICT(recipient_player_id,source_key) WHERE source_key IS NOT NULL DO NOTHING",c,t))
        {
            mail.Parameters.AddWithValue(playerId);mail.Parameters.AddWithValue(MailTitle);mail.Parameters.AddWithValue(body);mail.Parameters.AddWithValue(sourceKey);
            await mail.ExecuteNonQueryAsync(ct);
        }

        // Legacy decideBoxInfo emits TaskMessageWorldTreasureByType after the reward/mail and before persisting boxispicked=0.
        await QuestEventLedger.RecordCurrentAsync(c,t,playerId,"world_treasure_type",treasure.Type,ct);

        await using(var picked=new NpgsqlCommand(@"UPDATE player_world_treasure_boxes
SET picked_at=now()
WHERE player_id=$1 AND road_id=$2 AND treasure_id=$3 AND picked_at IS NULL",c,t))
        {
            picked.Parameters.AddWithValue(playerId);picked.Parameters.AddWithValue(roadId);picked.Parameters.AddWithValue(treasureId);
            if(await picked.ExecuteNonQueryAsync(ct)!=1)
                throw new GameException("WORLD_TREASURE_PICK_CHANGED","World treasure box state changed during reward grant.",409);
        }

        // PostgreSQL NOTIFY is transaction-aware: it is delivered only when the enclosing World transaction commits.
        // Payload mirrors legacy PUSH_ATTMOV.curReward without altering the authoritative WorldResponse contract.
        var curReward=BuildCurReward(content,kind,value);
        var notification=JsonSerializer.Serialize(new Dictionary<string,object>
        {
            ["playerId"]=playerId,
            ["curReward"]=new[]{curReward}
        });
        await using var notify=new NpgsqlCommand($"SELECT pg_notify('{RewardNotificationChannel}',$1)",c,t);
        notify.Parameters.AddWithValue(notification);
        await notify.ExecuteNonQueryAsync(ct);
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

    static Dictionary<string,object> BuildCurReward(CanonicalContent content,string kind,int value)
    {
        if(kind=="equip")
        {
            if(!content.Equipment.TryGetValue(value,out var equip))
                throw new GameException("WORLD_TREASURE_EQUIPMENT_MISSING",$"Legacy reward equipment {value} is missing.",500);
            return new Dictionary<string,object>
            {
                ["type"]=31,
                ["equipName"]=equip.Name,
                ["pic"]=equip.Pic,
                ["intro"]=equip.Intro,
                ["quality"]=equip.Quality
            };
        }
        var type=kind switch
        {
            "copper"=>1,
            "lumber"=>2,
            "food"=>3,
            "gold"=>19,
            _=>throw new GameException("WORLD_TREASURE_REWARD_UNSUPPORTED",$"Unsupported legacy WorldTreasure reward: {kind}.",500)
        };
        return new Dictionary<string,object>{{"type",type},{"num",value}};
    }

    static async Task<string> GrantAsync(NpgsqlConnection c,NpgsqlTransaction t,CanonicalContent content,long playerId,string kind,int value,CancellationToken ct)
    {
        switch(kind)
        {
            case "copper":
                await AddResourceAsync(c,t,playerId,"copper",value,ct);return $"{value}银币";
            case "lumber":
                await AddResourceAsync(c,t,playerId,"wood",value,ct);return $"{value}木材";
            case "food":
                await AddResourceAsync(c,t,playerId,"food",value,ct);return $"{value}粮食";
            case "gold":
                await using(var gold=new NpgsqlCommand("UPDATE players SET sys_gold=sys_gold+$2,updated_at=now() WHERE id=$1",c,t))
                {gold.Parameters.AddWithValue(playerId);gold.Parameters.AddWithValue(value);if(await gold.ExecuteNonQueryAsync(ct)!=1)throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);}
                return $"{value}金币";
            case "equip":
                return await GrantEquipmentAsync(c,t,content,playerId,value,ct);
            default:
                throw new GameException("WORLD_TREASURE_REWARD_UNSUPPORTED",$"Unsupported legacy WorldTreasure reward: {kind}.",500);
        }
    }

    static async Task AddResourceAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,string column,int value,CancellationToken ct)
    {
        await using var cmd=new NpgsqlCommand($"UPDATE player_resources SET {column}={column}+$2 WHERE player_id=$1",c,t);
        cmd.Parameters.AddWithValue(playerId);cmd.Parameters.AddWithValue(value);
        if(await cmd.ExecuteNonQueryAsync(ct)!=1)throw new GameException("PLAYER_RESOURCE_NOT_FOUND","Player resource row does not exist.",500);
    }

    static async Task<string> GrantEquipmentAsync(NpgsqlConnection c,NpgsqlTransaction t,CanonicalContent content,long playerId,int equipmentId,CancellationToken ct)
    {
        if(!content.Equipment.TryGetValue(equipmentId,out var equip))
            throw new GameException("WORLD_TREASURE_EQUIPMENT_MISSING",$"Legacy reward equipment {equipmentId} is missing.",500);
        int maxStoreNum;
        await using(var player=new NpgsqlCommand("SELECT max_store_num FROM players WHERE id=$1",c,t))
        {player.Parameters.AddWithValue(playerId);var value=await player.ExecuteScalarAsync(ct);if(value is null or DBNull)throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);maxStoreNum=Convert.ToInt32(value);}
        int itemCount;
        await using(var count=new NpgsqlCommand("SELECT count(*) FROM player_equipment WHERE player_id=$1",c,t))
        {count.Parameters.AddWithValue(playerId);itemCount=Convert.ToInt32(await count.ExecuteScalarAsync(ct));}
        if(itemCount<maxStoreNum)
        {
            await using var add=new NpgsqlCommand(@"INSERT INTO player_equipment(player_id,equipment_id,goods_type,level,quality,attribute,owner_general_id,refresh_attribute,gem_id,quenching_times,state,num)
VALUES($1,$2,$3,$4,$5,$6,NULL,'',0,0,0,1)",c,t);
            add.Parameters.AddWithValue(playerId);add.Parameters.AddWithValue(equipmentId);add.Parameters.AddWithValue(equip.Type);add.Parameters.AddWithValue(equip.DefaultLevel);add.Parameters.AddWithValue(equip.Quality);add.Parameters.AddWithValue(equip.Attribute);
            await add.ExecuteNonQueryAsync(ct);
        }
        else
        {
            await using var overflow=new NpgsqlCommand(@"INSERT INTO player_storehouse_sell(player_id,item_id,type,goods_type,level,attribute,quality,sell_time,gem_id,num,refresh_attribute,quenching_times,quenching_times_free,special_skill_id)
VALUES($1,$2,1,$3,$4,$5,$6,now(),0,1,'',0,0,0)",c,t);
            overflow.Parameters.AddWithValue(playerId);overflow.Parameters.AddWithValue(equipmentId);overflow.Parameters.AddWithValue(equip.Type);overflow.Parameters.AddWithValue(equip.DefaultLevel);overflow.Parameters.AddWithValue(equip.Attribute.ToString(CultureInfo.InvariantCulture));overflow.Parameters.AddWithValue(equip.Quality);
            await overflow.ExecuteNonQueryAsync(ct);
        }
        return equip.Name;
    }

    static IReadOnlyDictionary<int,WorldTreasureDefinition> Definitions(CanonicalContent content)=>DefinitionsByDirectory.GetOrAdd(content.BaseDirectory,static dir=>
    {
        var path=Path.Combine(dir,"world_treasures.json");
        var data=JsonSerializer.Deserialize<WorldTreasureDefinition[]>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})
            ?? throw new InvalidOperationException("Cannot load world_treasures.json.");
        return data.ToDictionary(x=>x.Id);
    });

    static (string kind,int value) ParseReward(string reward)
    {
        var parts=reward.Split(',',StringSplitOptions.TrimEntries);
        if(parts.Length!=2||!int.TryParse(parts[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out var value)||value<=0)
            throw new GameException("WORLD_TREASURE_REWARD_INVALID",$"Invalid legacy WorldTreasure reward: {reward}.",500);
        return(parts[0].ToLowerInvariant(),value);
    }

    static int TreasureId(WorldRoadDefinition road,int force)
    {
        var raw=force switch{1=>road.WeiReward,2=>road.ShuReward,3=>road.WuReward,_=>""};
        return string.IsNullOrWhiteSpace(raw)?0:int.Parse(raw,CultureInfo.InvariantCulture);
    }
}

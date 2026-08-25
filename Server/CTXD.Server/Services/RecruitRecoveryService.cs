using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CTXD.Server.Data;
using CTXD.Server.Domain;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record RecruitRecoveryResult(
    bool Full,int Forces,int MaxForces,int TokensConsumed,int TokensRemaining,long FoodConsumed,string Reason);

public sealed class RecruitRecoveryService(
    GameDb db,
    CanonicalContent content,
    TechnologyEffectService technologies,
    ResourceProductionService production)
{
    const int RecruitTokenMax=100;
    const int TokenChargeItemId=13;
    const int VipTokenChargeItemId=57;
    static readonly TimeSpan LegacyOffset=TimeSpan.FromHours(8);

    sealed record PB(int Id,int Level);
    sealed class WorldCityAreaRow
    {
        public int Area { get; set; }
        [JsonPropertyName("troop_conscribe_speed")] public int TroopConscribeSpeed { get; set; }
    }
    sealed class TroopConscribeSpeedRow
    {
        public int Level { get; set; }
        [JsonPropertyName("speed_muti_e")] public double SpeedMultiplier { get; set; }
    }

    readonly IReadOnlyDictionary<int,WorldCityAreaRow> areas=Load<WorldCityAreaRow[]>(content.BaseDirectory,"world_city_area.json").ToDictionary(x=>x.Area);
    readonly IReadOnlyDictionary<int,TroopConscribeSpeedRow> speedLevels=Load<TroopConscribeSpeedRow[]>(content.BaseDirectory,"troop_conscribe_speed.json").ToDictionary(x=>x.Level);

    public async Task<RecruitRecoveryResult> RecoverWithTokensAsync(long playerId,int generalId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);

        int force,consumeLevel;
        await using(var player=new NpgsqlCommand("SELECT force_id,consume_level FROM players WHERE id=$1 FOR UPDATE",c,t))
        {
            player.Parameters.AddWithValue(playerId);
            await using var r=await player.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);
            force=r.GetInt16(0);consumeLevel=r.GetInt32(1);
        }

        var tokens=await EnsureRecruitTokensAsync(c,t,playerId,consumeLevel,ct);

        int level,forces,state,location;
        DateTimeOffset recruitUpdatedAt;
        await using(var general=new NpgsqlCommand(@"
SELECT level,forces,state,location_id,recruit_updated_at
FROM player_generals
WHERE player_id=$1 AND general_id=$2 AND general_type=2
FOR UPDATE",c,t))
        {
            general.Parameters.AddWithValue(playerId);general.Parameters.AddWithValue(generalId);
            await using var r=await general.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw new GameException("RECRUIT_GENERAL_MISSING","Military general does not exist.",404);
            level=r.GetInt32(0);forces=r.GetInt32(1);state=r.GetInt16(2);location=r.GetInt32(3);recruitUpdatedAt=r.GetFieldValue<DateTimeOffset>(4);
        }

        var max=await MaxForcesAsync(c,t,playerId,generalId,level,ct);
        if(forces>=max)
        {
            await t.CommitAsync(ct);
            return new(true,forces,max,0,tokens,0,"full");
        }
        if(state>1)
        {
            await t.CommitAsync(ct);
            return new(false,forces,max,0,tokens,0,"busy");
        }
        if(tokens<=0)
        {
            await t.CommitAsync(ct);
            return new(false,forces,max,0,tokens,0,"no-token");
        }
        if(!content.Generals.TryGetValue(generalId,out var generalDef) || !content.TroopConscripts.TryGetValue(generalDef.TroopId,out var conscribe))
            throw new GameException("RECRUIT_STATIC_MISSING","Legacy troop conscription data is missing.",500);
        if(!content.WorldCities.TryGetValue(location,out var city))
            throw new GameException("RECRUIT_CITY_MISSING","General is not located in a canonical world city.",500);

        var output=await RecruitOutputPerSecondAsync(c,t,playerId,force,city,ct);
        var tokenMinutes=Charge(TokenChargeItemId).Param;
        var tokenCapacity=(long)(output*tokenMinutes*60d);
        if(tokenCapacity<=0)throw new GameException("RECRUIT_OUTPUT_INVALID","Legacy recruit output must be positive.",500);

        var remaining=max-forces;
        var elapsed=Math.Max(0d,(DateTimeOffset.UtcNow-recruitUpdatedAt).TotalSeconds);
        var passive=Math.Min((long)remaining,(long)(elapsed*output));
        var deficit=Math.Max(0L,remaining-passive);
        var needTokens=deficit==0?0:(int)Math.Ceiling(deficit/(double)tokenCapacity);
        var full=true;
        var recover=remaining;

        // Exact legacy cdRecoverConfirm partial-token branch: when available tokens are
        // insufficient it replaces the recovery amount with token capacity and deliberately
        // leaves UPDATE_FORCES_TIME untouched (addGeneralForces2).
        if(tokens<needTokens)
        {
            needTokens=tokens;
            var paidCapacity=(long)needTokens*tokenCapacity;
            if(paidCapacity<remaining)
            {
                recover=(int)paidCapacity;
                full=false;
            }
        }

        var distance=force switch{1=>city.WeiDistance,2=>city.ShuDistance,3=>city.WuDistance,_=>throw new GameException("RECRUIT_FORCE_INVALID","Player force is invalid for world recruitment.",409)};
        var consumeExponent=ConstantDouble("World.TroopConscribe.Consume.E");
        var foodPerForce=conscribe.Food*(1d+consumeExponent*distance);
        var foodCost=(long)(recover*foodPerForce);

        await production.AccrueAndGetAsync(playerId,ct,c,t);
        if(foodCost>0)
        {
            await using var pay=new NpgsqlCommand("UPDATE player_resources SET food=food-$2 WHERE player_id=$1 AND food>=$2",c,t);
            pay.Parameters.AddWithValue(playerId);pay.Parameters.AddWithValue(foodCost);
            if(await pay.ExecuteNonQueryAsync(ct)==0)
            {
                await t.CommitAsync(ct);
                return new(false,forces,max,0,tokens,0,"food");
            }
        }

        if(needTokens>0)
        {
            await using var token=new NpgsqlCommand(@"
UPDATE player_recruit_runtime
SET recruit_token=recruit_token-$2,updated_at=now()
WHERE player_id=$1 AND recruit_token>=$2",c,t);
            token.Parameters.AddWithValue(playerId);token.Parameters.AddWithValue(needTokens);
            if(await token.ExecuteNonQueryAsync(ct)==0)throw new GameException("RECRUIT_TOKEN_RACE","Recruit tokens changed concurrently.",409);
            tokens-=needTokens;
        }

        if(full)
        {
            await using var add=new NpgsqlCommand(@"
UPDATE player_generals
SET forces=LEAST($3,forces+$4),state=0,recruit_updated_at=now(),updated_at=now()
WHERE player_id=$1 AND general_id=$2",c,t);
            add.Parameters.AddWithValue(playerId);add.Parameters.AddWithValue(generalId);add.Parameters.AddWithValue(max);add.Parameters.AddWithValue(recover);
            await add.ExecuteNonQueryAsync(ct);
        }
        else
        {
            await using var add=new NpgsqlCommand(@"
UPDATE player_generals
SET forces=forces+$3,updated_at=now()
WHERE player_id=$1 AND general_id=$2",c,t);
            add.Parameters.AddWithValue(playerId);add.Parameters.AddWithValue(generalId);add.Parameters.AddWithValue(recover);
            await add.ExecuteNonQueryAsync(ct);
        }

        forces=Math.Min(max,forces+recover);
        await t.CommitAsync(ct);
        return new(forces>=max,forces,max,needTokens,tokens,foodCost,full?"recovered":"partial");
    }

    async Task<int> EnsureRecruitTokensAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int consumeLevel,CancellationToken ct)
    {
        var today=LegacyDay();
        var baseDaily=ConstantInt("Base.MuBingLing.Daily");
        await using(var insert=new NpgsqlCommand(@"
INSERT INTO player_recruit_runtime(player_id,recruit_token,reset_day)
VALUES($1,$2,$3)
ON CONFLICT(player_id) DO NOTHING",c,t))
        {
            insert.Parameters.AddWithValue(playerId);insert.Parameters.AddWithValue(baseDaily);insert.Parameters.AddWithValue(today);
            await insert.ExecuteNonQueryAsync(ct);
        }

        int tokens;DateOnly resetDay;
        await using(var read=new NpgsqlCommand("SELECT recruit_token,reset_day FROM player_recruit_runtime WHERE player_id=$1 FOR UPDATE",c,t))
        {
            read.Parameters.AddWithValue(playerId);await using var r=await read.ExecuteReaderAsync(ct);await r.ReadAsync(ct);
            tokens=r.GetInt32(0);resetDay=r.GetFieldValue<DateOnly>(1);
        }
        if(resetDay>=today)return tokens;

        var vip=Charge(VipTokenChargeItemId);
        var daily=baseDaily+(consumeLevel>=vip.Level?vip.Param:0);
        await using var reset=new NpgsqlCommand(@"
UPDATE player_recruit_runtime
SET recruit_token=GREATEST(recruit_token,LEAST($2,recruit_token+$3)),reset_day=$4,updated_at=now()
WHERE player_id=$1
RETURNING recruit_token",c,t);
        reset.Parameters.AddWithValue(playerId);reset.Parameters.AddWithValue(RecruitTokenMax);reset.Parameters.AddWithValue(daily);reset.Parameters.AddWithValue(today);
        return Convert.ToInt32(await reset.ExecuteScalarAsync(ct));
    }

    async Task<double> RecruitOutputPerSecondAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int force,WorldCityDefinition city,CancellationToken ct)
    {
        var buildings=await ReadBuildingsAsync(c,t,playerId,ct);
        var total=0;
        foreach(var pb in buildings.Values)
        {
            if(!content.Buildings.TryGetValue(pb.Id,out var b)||b.AreaType!=5||b.OutputType==4)continue;
            total+=BuildingOutput(buildings,b,pb.Level,new HashSet<int>());
        }
        total+=ConstantInt("Troop.Conscribe.BaseSpeed");

        // Legacy BuildingOutputCache: type 5 has no officer output. player_resource_addition
        // is not present in the remake runtime, therefore its current contribution is exactly 0.
        var tech8=await technologies.GetCompletedIntEffectAsync(playerId,8,0,ct,c,t);
        if(tech8!=0)total+=(int)(total*(tech8/100d));
        var speedLevel=await technologies.GetCompletedIntEffectAsync(playerId,28,0,ct,c,t)+1;
        if(!speedLevels.TryGetValue(speedLevel,out var speed)||speed.SpeedMultiplier<=0)
            throw new GameException("RECRUIT_SPEED_LEVEL_MISSING",$"Legacy troop conscribe speed level {speedLevel} is missing.",500);
        total=(int)(total/speed.SpeedMultiplier);

        var areaId=force switch{1=>city.WeiArea,2=>city.ShuArea,3=>city.WuArea,_=>0};
        if(!areas.TryGetValue(areaId,out var area))
            throw new GameException("RECRUIT_AREA_MISSING",$"Legacy world city area {areaId} is missing.",500);
        return total/3600d*area.TroopConscribeSpeed/100d;
    }

    async Task<int> MaxForcesAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int generalId,int level,CancellationToken ct)
    {
        int equipmentHp;
        await using(var equip=new NpgsqlCommand(@"
SELECT COALESCE(sum(CASE WHEN goods_type NOT IN(1,2,3,4,10,14) THEN attribute ELSE 0 END),0)
FROM player_equipment
WHERE player_id=$1 AND owner_general_id=$2",c,t))
        {
            equip.Parameters.AddWithValue(playerId);equip.Parameters.AddWithValue(generalId);
            equipmentHp=Convert.ToInt32(await equip.ExecuteScalarAsync(ct));
        }
        var techHp=await technologies.GetCompletedIntEffectAsync(playerId,30,2,ct,c,t);
        var columns=2+await technologies.GetCompletedIntEffectAsync(playerId,4,0,ct,c,t);
        var hp=(1200+(level-1)*24+equipmentHp+techHp)/3*Math.Max(1,columns)*3;
        return hp-hp%6;
    }

    async Task<Dictionary<int,PB>> ReadBuildingsAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,CancellationToken ct)
    {
        var result=new Dictionary<int,PB>();
        await using var q=new NpgsqlCommand("SELECT building_id,level FROM player_buildings WHERE player_id=$1",c,t);
        q.Parameters.AddWithValue(playerId);await using var r=await q.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)){var pb=new PB(r.GetInt32(0),r.GetInt32(1));result[pb.Id]=pb;}
        return result;
    }

    int BuildingOutput(Dictionary<int,PB> pbs,BuildingDefinition b,int level,HashSet<int> path)
    {
        if(!path.Add(b.Id))return 0;
        try
        {
            if(b.OutputType is 1 or 4 or 5)return (int)(b.OutputExponent*content.Serial(b.OutputSeriesId,level));
            if(b.OutputType is 2 or 3)
            {
                var related=0;
                foreach(var id in b.OutputRelatedBuildings)
                    if(pbs.TryGetValue(id,out var pb)&&content.Buildings.TryGetValue(id,out var rb))related+=BuildingOutput(pbs,rb,pb.Level,path);
                return (int)(b.OutputExponent*content.Serial(b.OutputSeriesId,level)+b.OutputRelatedFactor*related);
            }
            return 0;
        }
        finally{path.Remove(b.Id);}
    }

    ChargeItemDefinition Charge(int id)=>content.ChargeItems.TryGetValue(id,out var item)?item:throw new GameException("RECRUIT_CHARGEITEM_MISSING",$"Legacy chargeitem {id} is missing.",500);
    int ConstantInt(string key)=>(int)ConstantDouble(key);
    double ConstantDouble(string key)
    {
        if(!content.Constants.TryGetValue(key,out var value)||!double.TryParse(value.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var number))
            throw new GameException("RECRUIT_CONSTANT_MISSING",$"Legacy constant {key} is missing.",500);
        return number;
    }
    static DateOnly LegacyDay()=>DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(LegacyOffset).Date);
    static T Load<T>(string dir,string file)=>JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(dir,file)))??throw new InvalidOperationException($"Cannot load {file}");
}

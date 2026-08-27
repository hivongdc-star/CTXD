using System.Text.Json;
using System.Text.Json.Serialization;
using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record FarmOption(int Type,int GeneralState,int Minutes,int Food,int Reward);
public sealed record FarmGeneralView(int GeneralId,int State,int LocationId,int? Type,DateTimeOffset? EndsAt,int Reward,long BuffCd);
public sealed record FarmView(int ForceId,int FarmCityId,int NationLevel,int FarmLevel,long InvestSum,int NextUpCopper,bool CanInvest,long InvestCd,int RecoverGold,int ItemNumber,int Coefficient,int CityExpBonus,FarmOption[] Options,FarmGeneralView[] Generals);
public sealed record FarmInvestResult(int FarmLevel,long InvestSum,long Cd,int Exp);
public sealed record FarmStartRequest(int GeneralId,int Type);
public sealed record FarmStartResult(int GeneralId,int Type,int Food,int Reward,DateTimeOffset EndsAt);
public sealed record FarmRewardResult(int GeneralId,int Type,int Reward,int Gold,long BuffCd);
public sealed record FarmGoldRequest(string RequestKey);

public sealed class FarmService(
    GameDb db,
    CanonicalContent content,
    ResourceProductionService production,
    ExperienceService experience,
    IPlayerItemInventory items,
    DstqActivityService dstq,
    GamePushHub push)
{
    const int OpenLevel=30,InvestCopper=10_000,InvestExp=1_000,FarmTokenItem=1701,FarmTokenType=20;
    const int FarmIdleState=24,FarmBuffPercent=50;
    static readonly TimeSpan InvestStep=TimeSpan.FromMinutes(10),InvestMax=TimeSpan.FromHours(1),BuffDuration=TimeSpan.FromMinutes(30);
    static readonly IReadOnlyDictionary<int,int> FarmCities=new Dictionary<int,int>{{1,254},{2,253},{3,206}};

    sealed class FarmDef
    {
        [JsonPropertyName("lv")] public int Level{get;set;}
        [JsonPropertyName("name")] public string Name{get;set;}="";
        [JsonPropertyName("nation_lv")] public int NationLevel{get;set;}
        [JsonPropertyName("up_copper")] public int UpCopper{get;set;}
        [JsonPropertyName("food_reward")] public int FoodReward{get;set;}
        [JsonPropertyName("food_time")] public int FoodTime{get;set;}
        [JsonPropertyName("exp_reward")] public int ExpReward{get;set;}
        [JsonPropertyName("exp_time")] public int ExpTime{get;set;}
        [JsonPropertyName("exp_extra")] public int ExpExtra{get;set;}
        [JsonPropertyName("consume_food")] public int ConsumeFood{get;set;}
        [JsonPropertyName("exp_extra2")] public int ExpExtra2{get;set;}
        [JsonPropertyName("consume_food2")] public int ConsumeFood2{get;set;}
    }
    sealed class FarmCoeDef
    {
        [JsonPropertyName("lv_low")] public int Low{get;set;}
        [JsonPropertyName("lv_high")] public int High{get;set;}
        [JsonPropertyName("coe")] public int Coefficient{get;set;}
    }
    sealed record PlayerMeta(int Level,int Force);
    sealed record FarmRow(long Id,int GeneralId,int Type,DateTimeOffset StartedAt,DateTimeOffset EndsAt,int Reward,int DurationMinutes);

    readonly IReadOnlyDictionary<int,FarmDef> farms=Load<FarmDef[]>(content.BaseDirectory,"farm.json").ToDictionary(x=>x.Level);
    readonly FarmCoeDef[] coefficients=Load<FarmCoeDef[]>(content.BaseDirectory,"farm_coe.json");

    public async Task<FarmView> GetAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        var p=await PlayerAsync(c,t,playerId,true,ct);RequireOpen(p.Level);var city=FarmCity(p.Force);
        await EnsureAsync(c,t,playerId,p.Force,ct);
        int nationLevel,farmLevel;long invest;await using(var q=new NpgsqlCommand("SELECT n.level,f.level,f.invest_sum FROM nation_forces n JOIN nation_farm_state f ON f.force_id=n.force_id WHERE n.force_id=$1",c,t)){q.Parameters.AddWithValue(p.Force);await using var r=await q.ExecuteReaderAsync(ct);await r.ReadAsync(ct);nationLevel=r.GetInt32(0);farmLevel=r.GetInt32(1);invest=r.GetInt64(2);}
        DateTimeOffset cd;await using(var q=new NpgsqlCommand("SELECT invest_cd_until FROM player_farm_runtime WHERE player_id=$1",c,t)){q.Parameters.AddWithValue(playerId);cd=(DateTimeOffset)(await q.ExecuteScalarAsync(ct))!;}
        var itemNumber=await ItemCountAsync(c,t,playerId,ct);var coe=Coefficient(p.Level);var bonus=await CityExpBonusAsync(c,t,p.Force,ct);var current=Farm(farmLevel);var options=Options(current,coe,bonus);
        var next=farms.GetValueOrDefault(farmLevel+1);var now=DateTimeOffset.UtcNow;var rows=new List<FarmGeneralView>();
        await using(var q=new NpgsqlCommand(@"SELECT g.general_id,g.state,g.location_id,f.type,f.ends_at,f.reward,b.expires_at FROM player_generals g LEFT JOIN player_farms f ON f.player_id=g.player_id AND f.general_id=g.general_id LEFT JOIN player_farm_buffs b ON b.player_id=g.player_id AND b.general_id=g.general_id WHERE g.player_id=$1 AND g.general_type=2 ORDER BY g.general_id",c,t)){q.Parameters.AddWithValue(playerId);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct)){var buff=r.IsDBNull(6)?0:Math.Max(0,(long)(r.GetFieldValue<DateTimeOffset>(6)-now).TotalMilliseconds);rows.Add(new(r.GetInt32(0),r.GetInt16(1),r.GetInt32(2),r.IsDBNull(3)?null:r.GetInt16(3),r.IsDBNull(4)?null:r.GetFieldValue<DateTimeOffset>(4),r.IsDBNull(5)?0:r.GetInt32(5),buff));}}
        await t.CommitAsync(ct);var remaining=Math.Max(0,(long)(cd-now).TotalMilliseconds);return new(p.Force,city,nationLevel,farmLevel,invest,next?.UpCopper??0,next is not null&&nationLevel>=next.NationLevel,remaining,RecoverCost(cd,78),itemNumber,coe,bonus,options,rows.ToArray());
    }

    public async Task<FarmInvestResult> InvestAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);var p=await PlayerAsync(c,t,playerId,true,ct);RequireOpen(p.Level);await EnsureAsync(c,t,playerId,p.Force,ct);
        int nationLevel,farmLevel;long sum;await using(var q=new NpgsqlCommand("SELECT n.level,f.level,f.invest_sum FROM nation_forces n JOIN nation_farm_state f ON f.force_id=n.force_id WHERE n.force_id=$1 FOR UPDATE OF f",c,t)){q.Parameters.AddWithValue(p.Force);await using var r=await q.ExecuteReaderAsync(ct);await r.ReadAsync(ct);nationLevel=r.GetInt32(0);farmLevel=r.GetInt32(1);sum=r.GetInt64(2);}
        if(!farms.TryGetValue(farmLevel+1,out var next))throw new GameException("FARM_MAX_LEVEL","Farm already reached legacy max level.",409);if(nationLevel<next.NationLevel)throw new GameException("FARM_NATION_LEVEL_REQUIRED","Nation level is too low for the next Farm level.",409);
        DateTimeOffset cd;await using(var q=new NpgsqlCommand("SELECT invest_cd_until FROM player_farm_runtime WHERE player_id=$1 FOR UPDATE",c,t)){q.Parameters.AddWithValue(playerId);cd=(DateTimeOffset)(await q.ExecuteScalarAsync(ct))!;}var now=DateTimeOffset.UtcNow;if(cd-now>InvestMax)throw new GameException("FARM_INVEST_CD_MAX","Farm investment cooldown queue reached the legacy one-hour cap.",409);
        await production.AccrueAndGetAsync(playerId,ct,c,t);await using(var pay=new NpgsqlCommand("UPDATE player_resources SET copper=copper-$2 WHERE player_id=$1 AND copper>=$2",c,t)){pay.Parameters.AddWithValue(playerId);pay.Parameters.AddWithValue(InvestCopper);if(await pay.ExecuteNonQueryAsync(ct)!=1)throw new GameException("FARM_COPPER_NOT_ENOUGH","Farm investment requires 10,000 copper.",409);}
        sum+=InvestCopper;if(sum>=next.UpCopper){farmLevel++;sum=0;}await using(var save=new NpgsqlCommand("UPDATE nation_farm_state SET level=$2,invest_sum=$3,updated_at=now() WHERE force_id=$1;UPDATE player_farm_runtime SET invest_cd_until=$5,updated_at=now() WHERE player_id=$4",c,t)){save.Parameters.AddWithValue(p.Force);save.Parameters.AddWithValue(farmLevel);save.Parameters.AddWithValue(sum);save.Parameters.AddWithValue(playerId);save.Parameters.AddWithValue((cd>now?cd:now).Add(InvestStep));await save.ExecuteNonQueryAsync(ct);}await experience.AddAsync(c,t,playerId,InvestExp,ct);await t.CommitAsync(ct);await push.SendAsync(playerId,"farm.updated",new{kind="invest",farmLevel},ct);var view=await GetAsync(playerId,ct);return new(farmLevel,sum,view.InvestCd,InvestExp);
    }

    public async Task<int> RecoverInvestAsync(long playerId,string requestKey,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(requestKey))throw new GameException("FARM_REQUEST_INVALID","Request key is required.");await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);var p=await PlayerAsync(c,t,playerId,true,ct);RequireOpen(p.Level);await EnsureAsync(c,t,playerId,p.Force,ct);
        await using(var old=new NpgsqlCommand("SELECT gold_spent FROM player_farm_gold_actions WHERE player_id=$1 AND request_key=$2 AND action='recover-invest'",c,t)){old.Parameters.AddWithValue(playerId);old.Parameters.AddWithValue(requestKey);var v=await old.ExecuteScalarAsync(ct);if(v is not null){await t.CommitAsync(ct);return Convert.ToInt32(v);}}
        DateTimeOffset cd;await using(var q=new NpgsqlCommand("SELECT invest_cd_until FROM player_farm_runtime WHERE player_id=$1 FOR UPDATE",c,t)){q.Parameters.AddWithValue(playerId);cd=(DateTimeOffset)(await q.ExecuteScalarAsync(ct))!;}var cost=RecoverCost(cd,78);if(cost<=0)throw new GameException("FARM_INVEST_CD_READY","Farm investment cooldown already ended.",409);await PayGoldAsync(c,t,playerId,cost,ct);await using(var q=new NpgsqlCommand("UPDATE player_farm_runtime SET invest_cd_until=now(),updated_at=now() WHERE player_id=$1;INSERT INTO player_farm_gold_actions(player_id,request_key,action,gold_spent) VALUES($1,$2,'recover-invest',$3)",c,t)){q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(requestKey);q.Parameters.AddWithValue(cost);await q.ExecuteNonQueryAsync(ct);}await dstq.RecordGoldSpendAsync(c,t,playerId,cost,ct);await t.CommitAsync(ct);await dstq.PushAsync(playerId,ct);await push.SendAsync(playerId,"farm.updated",new{kind="investCd",gold=cost},ct);return cost;
    }

    public async Task<FarmStartResult> StartAsync(long playerId,int generalId,int type,CancellationToken ct)
    {
        if(type is<0 or>3)throw new GameException("FARM_TYPE_INVALID","Farm type must be between 0 and 3.");await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);var result=await StartCoreAsync(c,t,playerId,generalId,type,false,ct);await t.CommitAsync(ct);await push.SendAsync(playerId,"farm.updated",result,ct);return result;
    }

    public async Task<bool> AutoStartOnEnterAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int generalId,int force,CancellationToken ct)
    {
        var city=FarmCity(force);int level;await using(var p=new NpgsqlCommand("SELECT level FROM players WHERE id=$1",c,t)){p.Parameters.AddWithValue(playerId);level=Convert.ToInt32(await p.ExecuteScalarAsync(ct)??0);}if(level<OpenLevel){await SetGeneralStateAsync(c,t,playerId,generalId,FarmIdleState,ct);return false;}
        try{await StartCoreAsync(c,t,playerId,generalId,1,true,ct);return true;}catch(GameException ex)when(ex.Code is "FARM_TOKEN_NOT_ENOUGH" or "FARM_STATIC_MISSING"){await using var clear=new NpgsqlCommand("DELETE FROM player_farms WHERE player_id=$1 AND general_id=$2",c,t);clear.Parameters.AddWithValue(playerId);clear.Parameters.AddWithValue(generalId);await clear.ExecuteNonQueryAsync(ct);await SetGeneralStateAsync(c,t,playerId,generalId,FarmIdleState,ct);return false;}
    }

    public async Task<FarmRewardResult> StopAsync(long playerId,int generalId,CancellationToken ct)
    {await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);var result=await FinishCoreAsync(c,t,playerId,generalId,false,0,ct);await t.CommitAsync(ct);await push.SendAsync(playerId,"farm.updated",result,ct);return result;}

    public async Task<FarmRewardResult> ClaimAsync(long playerId,int generalId,string requestKey,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(requestKey))throw new GameException("FARM_REQUEST_INVALID","Request key is required.");await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        await using(var old=new NpgsqlCommand("SELECT general_id,farm_type,reward,gold_spent FROM player_farm_gold_actions WHERE player_id=$1 AND request_key=$2 AND action='claim'",c,t)){old.Parameters.AddWithValue(playerId);old.Parameters.AddWithValue(requestKey);await using var r=await old.ExecuteReaderAsync(ct);if(await r.ReadAsync(ct)){var result=new FarmRewardResult(r.GetInt32(0),r.GetInt16(1),r.GetInt32(2),r.GetInt32(3),(long)BuffDuration.TotalMilliseconds);await t.CommitAsync(ct);return result;}}
        var row=await FarmRowAsync(c,t,playerId,generalId,true,ct)??throw new GameException("FARM_NOT_ACTIVE","General has no active Farm work.",409);var cost=0;if(row.EndsAt>DateTimeOffset.UtcNow){cost=RecoverCost(row.EndsAt,86);await PayGoldAsync(c,t,playerId,cost,ct);await dstq.RecordGoldSpendAsync(c,t,playerId,cost,ct);}var result2=await FinishCoreAsync(c,t,playerId,generalId,true,cost,ct,row);await using(var ledger=new NpgsqlCommand("INSERT INTO player_farm_gold_actions(player_id,request_key,action,general_id,farm_type,reward,gold_spent) VALUES($1,$2,'claim',$3,$4,$5,$6)",c,t)){ledger.Parameters.AddWithValue(playerId);ledger.Parameters.AddWithValue(requestKey);ledger.Parameters.AddWithValue(generalId);ledger.Parameters.AddWithValue((short)result2.Type);ledger.Parameters.AddWithValue(result2.Reward);ledger.Parameters.AddWithValue(cost);await ledger.ExecuteNonQueryAsync(ct);}await t.CommitAsync(ct);if(cost>0)await dstq.PushAsync(playerId,ct);await push.SendAsync(playerId,"farm.updated",result2,ct);return result2;
    }

    public async Task<FarmRewardResult[]> StopAllAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);var ids=new List<int>();await using(var q=new NpgsqlCommand("SELECT general_id FROM player_farms WHERE player_id=$1 ORDER BY id FOR UPDATE",c,t)){q.Parameters.AddWithValue(playerId);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))ids.Add(r.GetInt32(0));}if(ids.Count==0)throw new GameException("FARM_NOT_ACTIVE","No active Farm work.",409);var results=new List<FarmRewardResult>();foreach(var id in ids)results.Add(await FinishCoreAsync(c,t,playerId,id,false,0,ct));await t.CommitAsync(ct);await push.SendAsync(playerId,"farm.updated",new{kind="stopAll",count=results.Count},ct);return results.ToArray();
    }

    public async Task<int> GetBuffAsync(NpgsqlConnection c,NpgsqlTransaction? t,long playerId,int generalId,CancellationToken ct)
    {await using var q=new NpgsqlCommand("SELECT 1 FROM player_farm_buffs WHERE player_id=$1 AND general_id=$2 AND expires_at>now()",c,t);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalId);return await q.ExecuteScalarAsync(ct)is null?0:FarmBuffPercent;}

    async Task<FarmStartResult> StartCoreAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int generalId,int type,bool autoEnter,CancellationToken ct)
    {
        var p=await PlayerAsync(c,t,playerId,true,ct);if(!autoEnter)RequireOpen(p.Level);await EnsureAsync(c,t,playerId,p.Force,ct);var city=FarmCity(p.Force);var desired=FarmIdleState+1+type;
        int state,location;await using(var q=new NpgsqlCommand("SELECT state,location_id FROM player_generals WHERE player_id=$1 AND general_id=$2 AND general_type=2 FOR UPDATE",c,t)){q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalId);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("FARM_GENERAL_MISSING","Military general does not exist.",404);state=r.GetInt16(0);location=r.GetInt32(1);}if(location!=city)throw new GameException("FARM_LOCATION_WRONG","General must be in the legacy Farm city.",409);
        var switching=!autoEnter&&state!=desired&&state>FarmIdleState;var compatibilityIdle=state<=1&&location==city;if(!autoEnter&&state==desired)throw new GameException("FARM_GENERAL_BUSY","General is already doing this Farm work.",409);if(!autoEnter&&!switching&&state!=FarmIdleState&&!compatibilityIdle)throw new GameException("FARM_GENERAL_BUSY","General cannot start Farm work in the current state.",409);
        int farmLevel;await using(var q=new NpgsqlCommand("SELECT level FROM nation_farm_state WHERE force_id=$1",c,t)){q.Parameters.AddWithValue(p.Force);farmLevel=Convert.ToInt32(await q.ExecuteScalarAsync(ct));}var def=Farm(farmLevel);var coe=Coefficient(p.Level);var food=type switch{2=>def.ConsumeFood*coe,3=>def.ConsumeFood2*coe,_=>0};var extra=type switch{2=>def.ExpExtra,3=>def.ExpExtra2,_=>0};if(food>0){await production.AccrueAndGetAsync(playerId,ct,c,t);await using var pay=new NpgsqlCommand("UPDATE player_resources SET food=food-$2 WHERE player_id=$1 AND food>=$2",c,t);pay.Parameters.AddWithValue(playerId);pay.Parameters.AddWithValue(food);if(await pay.ExecuteNonQueryAsync(ct)!=1)throw new GameException("FARM_FOOD_NOT_ENOUGH","Not enough food for Farm training.",409);}
        var consumeToken=!switching;if(consumeToken&&!await items.ConsumeAsync(c,t,playerId,FarmTokenItem,FarmTokenType,1,ct))throw new GameException("FARM_TOKEN_NOT_ENOUGH","Farm token 1701 is required.",409);var bonus=type==0?0:await CityExpBonusAsync(c,t,p.Force,ct);var reward=((type==0?def.FoodReward:def.ExpReward)+extra)*coe+bonus;var minutes=type==0?def.FoodTime:def.ExpTime;var now=DateTimeOffset.UtcNow;var end=now.AddMinutes(minutes);
        await using(var save=new NpgsqlCommand(@"INSERT INTO player_farms(player_id,general_id,type,started_at,ends_at,reward,duration_minutes) VALUES($1,$2,$3,$4,$5,$6,$7) ON CONFLICT(player_id,general_id) DO UPDATE SET type=$3,started_at=$4,ends_at=$5,reward=$6,duration_minutes=$7",c,t)){save.Parameters.AddWithValue(playerId);save.Parameters.AddWithValue(generalId);save.Parameters.AddWithValue((short)type);save.Parameters.AddWithValue(now);save.Parameters.AddWithValue(end);save.Parameters.AddWithValue(reward);save.Parameters.AddWithValue(minutes);await save.ExecuteNonQueryAsync(ct);}await SetGeneralStateAsync(c,t,playerId,generalId,desired,ct);return new(generalId,type,food,reward,end);
    }

    async Task<FarmRewardResult> FinishCoreAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int generalId,bool full,int gold,CancellationToken ct,FarmRow? existing=null)
    {
        var row=existing??await FarmRowAsync(c,t,playerId,generalId,true,ct)??throw new GameException("FARM_NOT_ACTIVE","General has no active Farm work.",409);var now=DateTimeOffset.UtcNow;var remaining=Math.Max(0,(row.EndsAt-now).TotalMilliseconds);var fraction=Math.Min(remaining/(row.DurationMinutes*60_000d),1d);var reward=full?row.Reward:(int)((1d-fraction)*row.Reward);if(row.Type==0){await using var add=new NpgsqlCommand("UPDATE player_resources SET food=food+$2,update_time=now() WHERE player_id=$1",c,t);add.Parameters.AddWithValue(playerId);add.Parameters.AddWithValue(reward);await add.ExecuteNonQueryAsync(ct);}else await AddGeneralExperienceAsync(c,t,playerId,generalId,reward,ct);
        await using(var q=new NpgsqlCommand("DELETE FROM player_farms WHERE id=$1;INSERT INTO player_farm_buffs(player_id,general_id,expires_at) VALUES($2,$3,$4) ON CONFLICT(player_id,general_id) DO UPDATE SET expires_at=$4",c,t)){q.Parameters.AddWithValue(row.Id);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalId);q.Parameters.AddWithValue(now.Add(BuffDuration));await q.ExecuteNonQueryAsync(ct);}await SetGeneralStateAsync(c,t,playerId,generalId,FarmIdleState,ct);return new(generalId,row.Type,reward,gold,(long)BuffDuration.TotalMilliseconds);
    }

    async Task AddGeneralExperienceAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int generalId,int amount,CancellationToken ct)
    {if(amount<=0||!content.Generals.TryGetValue(generalId,out var def))return;int playerLevel,level;long exp;await using(var q=new NpgsqlCommand("SELECT p.level,g.level,g.exp FROM players p JOIN player_generals g ON g.player_id=p.id AND g.general_id=$2 WHERE p.id=$1 FOR UPDATE OF p,g",c,t)){q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalId);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return;playerLevel=r.GetInt32(0);level=r.GetInt32(1);exp=r.GetInt64(2);}exp+=amount;while(level<playerLevel){int need;try{need=content.Serial(def.UpgradeExpSeriesId,level);}catch{break;}if(exp<need)break;exp-=need;level++;}if(level>=playerLevel){try{exp=Math.Min(exp,content.Serial(def.UpgradeExpSeriesId,level));}catch{}}await using var save=new NpgsqlCommand("UPDATE player_generals SET level=$3,exp=$4,updated_at=now() WHERE player_id=$1 AND general_id=$2",c,t);save.Parameters.AddWithValue(playerId);save.Parameters.AddWithValue(generalId);save.Parameters.AddWithValue(level);save.Parameters.AddWithValue(exp);await save.ExecuteNonQueryAsync(ct);}

    async Task EnsureAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int force,CancellationToken ct)
    {await using var q=new NpgsqlCommand("INSERT INTO nation_farm_state(force_id) VALUES($1) ON CONFLICT DO NOTHING;INSERT INTO player_farm_runtime(player_id) VALUES($2) ON CONFLICT DO NOTHING",c,t);q.Parameters.AddWithValue(force);q.Parameters.AddWithValue(playerId);await q.ExecuteNonQueryAsync(ct);}
    async Task<PlayerMeta> PlayerAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,bool locked,CancellationToken ct){await using var q=new NpgsqlCommand("SELECT level,force_id FROM players WHERE id=$1"+(locked?" FOR UPDATE":""),c,t);q.Parameters.AddWithValue(playerId);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);return new(r.GetInt32(0),r.GetInt16(1));}
    async Task<FarmRow?> FarmRowAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int generalId,bool locked,CancellationToken ct){await using var q=new NpgsqlCommand("SELECT id,general_id,type,started_at,ends_at,reward,duration_minutes FROM player_farms WHERE player_id=$1 AND general_id=$2"+(locked?" FOR UPDATE":""),c,t);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalId);await using var r=await q.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?new(r.GetInt64(0),r.GetInt32(1),r.GetInt16(2),r.GetFieldValue<DateTimeOffset>(3),r.GetFieldValue<DateTimeOffset>(4),r.GetInt32(5),r.GetInt32(6)):null;}
    async Task<int> ItemCountAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,CancellationToken ct){await using var q=new NpgsqlCommand("SELECT COALESCE((SELECT quantity FROM player_items WHERE player_id=$1 AND item_id=$2 AND item_type=$3),0)",c,t);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(FarmTokenItem);q.Parameters.AddWithValue(FarmTokenType);return Convert.ToInt32(await q.ExecuteScalarAsync(ct));}
    async Task<int> CityExpBonusAsync(NpgsqlConnection c,NpgsqlTransaction t,int force,CancellationToken ct){var d=content.WorldCitySpecials.FirstOrDefault(x=>x.Key==3&&x.CityId<1000);if(d is null)return 0;await using var q=new NpgsqlCommand("SELECT 1 FROM world_cities WHERE city_id=$1 AND owner_force_id=$2",c,t);q.Parameters.AddWithValue(d.CityId);q.Parameters.AddWithValue(force);return await q.ExecuteScalarAsync(ct)is null?0:(int)d.Parameter2;}
    async Task PayGoldAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int cost,CancellationToken ct){if(cost<=0)return;await using var q=new NpgsqlCommand("UPDATE players SET sys_gold=sys_gold-$2 WHERE id=$1 AND sys_gold>=$2",c,t);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(cost);if(await q.ExecuteNonQueryAsync(ct)!=1)throw new GameException("FARM_GOLD_NOT_ENOUGH","Not enough gold.",409);}
    async Task SetGeneralStateAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int generalId,int state,CancellationToken ct){await using var q=new NpgsqlCommand("UPDATE player_generals SET state=$3,updated_at=now() WHERE player_id=$1 AND general_id=$2",c,t);q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(generalId);q.Parameters.AddWithValue((short)state);await q.ExecuteNonQueryAsync(ct);}
    FarmOption[] Options(FarmDef f,int coe,int bonus)=>[new(0,25,f.FoodTime,0,f.FoodReward*coe),new(1,26,f.ExpTime,0,f.ExpReward*coe+bonus),new(2,27,f.ExpTime,f.ConsumeFood*coe,(f.ExpReward+f.ExpExtra)*coe+bonus),new(3,28,f.ExpTime,f.ConsumeFood2*coe,(f.ExpReward+f.ExpExtra2)*coe+bonus)];
    int Coefficient(int level)=>coefficients.FirstOrDefault(x=>level>=x.Low&&level<=x.High)?.Coefficient??1;
    int RecoverCost(DateTimeOffset until,int chargeItemId){var ms=Math.Max(0,(until-DateTimeOffset.UtcNow).TotalMilliseconds);if(ms<=0)return 0;if(!content.ChargeItems.TryGetValue(chargeItemId,out var ci))throw new GameException("FARM_CHARGE_ITEM_MISSING",$"Legacy charge item {chargeItemId} is missing.",500);return (int)Math.Ceiling(ms/(ci.Param*60_000d))*ci.Cost;}
    FarmDef Farm(int level)=>farms.TryGetValue(level,out var f)?f:throw new GameException("FARM_STATIC_MISSING",$"Legacy Farm level {level} is missing.",500);
    static int FarmCity(int force)=>FarmCities.TryGetValue(force,out var city)?city:throw new GameException("FARM_FORCE_INVALID","Player force has no legacy Farm city.",409);
    static void RequireOpen(int level){if(level<OpenLevel)throw new GameException("FARM_LOCKED","Legacy Farm opens at player level 30.",403);}
    static T Load<T>(string dir,string file)=>JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(dir,file)),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException($"Cannot load {file}");
}

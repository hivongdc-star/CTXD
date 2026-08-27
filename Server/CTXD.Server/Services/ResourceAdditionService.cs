using System.Globalization;
using System.Text.Json.Serialization;
using CTXD.Server.Data;
using CTXD.Server.Domain;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record ResourceAdditionRateOption(int AdditionMode,double AdditionRate);
public sealed record ResourceAdditionDayOption(int TimeType,int TimeValue);
public sealed record ResourceAdditionPriceResult(long Price,ResourceAdditionRateOption[] Additions,ResourceAdditionDayOption[] Days);
public sealed record ResourceAdditionState(
    int AdditionMode,
    double AdditionRate,
    long AdditionCd,
    [property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] int? CurrentTimeType);
public sealed record ResourceAdditionBuyRequest(int AdditionMode,int TimeType,string RequestKey);
public sealed record ResourceAdditionUseItemRequest(int ItemId,string RequestKey);
public sealed record ResourceAdditionActivationResult(ResourceAdditionState State,long GoldSpent,int? ItemId,bool Replayed);

internal sealed class ResourceAdditionService
{
    const int ResourceFunctionId=41;
    const int RecruitResourceType=5;
    const int RecruitBuildingFunctionId=8;
    const int ResourceTokenItemType=13;
    const string PaidSource="paid";
    const string ItemSource="item";

    // Authoritative localized replacement for chargeitem.param. The current canonical model
    // stores Param as int and truncates the legacy DOUBLE value 1.5 for chargeitems 16/49.
    // Keep all resource-addition rate consumers on this single source of truth.
    static readonly double[] AdditionRates=[0d,1.5d,2d,3d];
    static readonly int[] DurationDaysByTimeType=[0,1,7,30];
    static readonly int[] NormalChargeItemIds=[0,16,17,18];
    static readonly int[] RecruitChargeItemIds=[0,49,50,51];
    static readonly int[] ResourceOpenFunctions=[0,0,5,6,7,8];

    sealed record PlayerGoldState(int ConsumeLevel,long SysGold,long UserGold);
    sealed record AdditionRow(int AdditionMode,int TimeType,DateTimeOffset EndsAt);
    sealed record RequestRow(string Source,int ResourceType,int AdditionMode,int TimeType,int? ChargeItemId,int? ItemId,long GoldSpent,DateTimeOffset EndsAt);

    public async Task<int> GetBuildingOutputContributionAsync(
        NpgsqlConnection c,
        NpgsqlTransaction t,
        long playerId,
        int resourceType,
        int baseOutput,
        CancellationToken ct)
    {
        if(baseOutput<=0)return 0;

        await using var q=new NpgsqlCommand(@"
SELECT addition_mode
FROM player_resource_additions
WHERE player_id=$1 AND resource_type=$2 AND ends_at>now()",c,t);
        q.Parameters.AddWithValue(playerId);
        q.Parameters.AddWithValue(resourceType);
        var raw=await q.ExecuteScalarAsync(ct);
        if(raw is null or DBNull)return 0;

        return (int)(baseOutput*(LegacyMultiplier(Convert.ToInt32(raw))-1d));
    }

    public ResourceAdditionPriceResult GetRecruitPrice(CanonicalContent content,int additionMode,int timeType)
        =>GetPrice(content,RecruitResourceType,additionMode,timeType);

    public async Task<ResourceAdditionState> GetRecruitStateAsync(GameDb db,long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        return await ReadStateAsync(c,null,playerId,RecruitResourceType,ct);
    }

    public async Task<ResourceAdditionActivationResult> BuyRecruitAsync(
        GameDb db,
        CanonicalContent content,
        long playerId,
        int additionMode,
        int timeType,
        string requestKey,
        CancellationToken ct)
    {
        ValidateRequestKey(requestKey);
        var resourceType=RecruitResourceType;
        var quote=GetPrice(content,resourceType,additionMode,timeType);
        var chargeItemId=ChargeItemId(resourceType,additionMode);
        var charge=Charge(content,chargeItemId);

        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);
        var player=await LockPlayerAsync(c,t,playerId,ct);

        var prior=await ReadRequestAsync(c,t,playerId,requestKey,ct);
        if(prior is not null)
        {
            if(prior.Source!=PaidSource||prior.ResourceType!=resourceType||prior.AdditionMode!=additionMode||prior.TimeType!=timeType||prior.ChargeItemId!=chargeItemId)
                throw new GameException("RESOURCE_ADDITION_REQUEST_CONFLICT","Request key was already used for another resource-addition action.",409);
            var replayState=await ReadStateAsync(c,t,playerId,resourceType,ct);
            await t.CommitAsync(ct);
            return new(replayState,prior.GoldSpent,null,true);
        }

        await RequireFunctionAsync(c,t,playerId,ResourceFunctionId,ct);
        if(player.ConsumeLevel<charge.Level)
            throw new GameException("RESOURCE_ADDITION_LEVEL_LOW",$"Legacy chargeitem {chargeItemId} requires consume level {charge.Level}.",403);

        var existing=await ReadAdditionForUpdateAsync(c,t,playerId,resourceType,ct);
        var endsAt=NextEnd(existing,additionMode,timeType);

        var sysSpent=Math.Min(player.SysGold,quote.Price);
        var userSpent=quote.Price-sysSpent;
        if(player.UserGold<userSpent)
            throw new GameException("RESOURCE_ADDITION_GOLD_NOT_ENOUGH","Not enough gold.",409);

        await using(var pay=new NpgsqlCommand(@"
UPDATE players
SET sys_gold=sys_gold-$2,user_gold=user_gold-$3,updated_at=now()
WHERE id=$1",c,t))
        {
            pay.Parameters.AddWithValue(playerId);
            pay.Parameters.AddWithValue(sysSpent);
            pay.Parameters.AddWithValue(userSpent);
            if(await pay.ExecuteNonQueryAsync(ct)!=1)throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);
        }

        await UpsertAsync(c,t,playerId,resourceType,additionMode,timeType,endsAt,ct);
        await InsertRequestAsync(c,t,playerId,requestKey,PaidSource,resourceType,additionMode,timeType,chargeItemId,null,quote.Price,endsAt,ct);
        await t.CommitAsync(ct);
        return new(State(additionMode,timeType,endsAt),quote.Price,null,false);
    }

    public async Task<ResourceAdditionActivationResult> UseRecruitItemAsync(
        GameDb db,
        CanonicalContent content,
        IPlayerItemInventory inventory,
        long playerId,
        int itemId,
        string requestKey,
        CancellationToken ct)
    {
        ValidateRequestKey(requestKey);
        if(!content.Items.TryGetValue(itemId,out var item)||item.Type!=ResourceTokenItemType)
            throw new GameException("RESOURCE_ADDITION_ITEM_INVALID","Item is not a legacy resource token.");
        var effect=ParseItemEffect(item.Effect);
        if(effect.ResourceType!=RecruitResourceType)
            throw new GameException("RESOURCE_ADDITION_ITEM_RESOURCE_UNSUPPORTED","This endpoint only activates the recruit resource addition.",409);

        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);
        _=await LockPlayerAsync(c,t,playerId,ct);

        var prior=await ReadRequestAsync(c,t,playerId,requestKey,ct);
        if(prior is not null)
        {
            if(prior.Source!=ItemSource||prior.ItemId!=itemId)
                throw new GameException("RESOURCE_ADDITION_REQUEST_CONFLICT","Request key was already used for another resource-addition action.",409);
            var replayState=await ReadStateAsync(c,t,playerId,RecruitResourceType,ct);
            await t.CommitAsync(ct);
            return new(replayState,0,itemId,true);
        }

        await RequireFunctionAsync(c,t,playerId,ResourceFunctionId,ct);
        await RequireFunctionAsync(c,t,playerId,RequiredResourceFunction(effect.ResourceType),ct);

        var existing=await ReadAdditionForUpdateAsync(c,t,playerId,effect.ResourceType,ct);
        if(existing is not null&&existing.EndsAt>DateTimeOffset.UtcNow&&existing.AdditionMode!=effect.AdditionMode)
            throw new GameException("RESOURCE_ADDITION_ACTIVE_MODE_CONFLICT","An active resource token can only be extended with the same addition mode.",409);
        var endsAt=NextEnd(existing,effect.AdditionMode,effect.TimeType);

        if(!await inventory.ConsumeAsync(c,t,playerId,item.Id,item.Type,1,ct))
            throw new GameException("RESOURCE_ADDITION_ITEM_MISSING","Resource token is not available.",409);

        await UpsertAsync(c,t,playerId,effect.ResourceType,effect.AdditionMode,effect.TimeType,endsAt,ct);
        await InsertRequestAsync(c,t,playerId,requestKey,ItemSource,effect.ResourceType,effect.AdditionMode,effect.TimeType,null,itemId,0,endsAt,ct);
        await t.CommitAsync(ct);
        return new(State(effect.AdditionMode,effect.TimeType,endsAt),0,itemId,false);
    }

    ResourceAdditionPriceResult GetPrice(CanonicalContent content,int resourceType,int additionMode,int timeType)
    {
        Validate(resourceType,additionMode,timeType);
        var charge=Charge(content,ChargeItemId(resourceType,additionMode));
        var days=DurationDays(timeType);
        var price=(long)(charge.Cost*days*Discount(content,timeType));
        return new(
            price,
            Enumerable.Range(1,3).Select(x=>new ResourceAdditionRateOption(x,LegacyMultiplier(x))).ToArray(),
            Enumerable.Range(1,3).Select(x=>new ResourceAdditionDayOption(x,DurationDays(x))).ToArray());
    }

    static async Task<PlayerGoldState> LockPlayerAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("SELECT consume_level,sys_gold,user_gold FROM players WHERE id=$1 FOR UPDATE",c,t);
        q.Parameters.AddWithValue(playerId);
        await using var r=await q.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);
        return new(r.GetInt32(0),r.GetInt64(1),r.GetInt64(2));
    }

    static async Task RequireFunctionAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int functionId,CancellationToken ct)
    {
        if(functionId==0)return;
        await using var q=new NpgsqlCommand("SELECT 1 FROM player_functions WHERE player_id=$1 AND function_id=$2",c,t);
        q.Parameters.AddWithValue(playerId);
        q.Parameters.AddWithValue(functionId);
        if(await q.ExecuteScalarAsync(ct)is null)
            throw new GameException("RESOURCE_ADDITION_FUNCTION_LOCKED",$"Legacy function {functionId} is not open.",403);
    }

    static async Task<AdditionRow?> ReadAdditionForUpdateAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int resourceType,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand(@"
SELECT addition_mode,time_type,ends_at
FROM player_resource_additions
WHERE player_id=$1 AND resource_type=$2
FOR UPDATE",c,t);
        q.Parameters.AddWithValue(playerId);
        q.Parameters.AddWithValue(resourceType);
        await using var r=await q.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)?new(r.GetInt16(0),r.GetInt16(1),r.GetFieldValue<DateTimeOffset>(2)):null;
    }

    static async Task<ResourceAdditionState> ReadStateAsync(NpgsqlConnection c,NpgsqlTransaction? t,long playerId,int resourceType,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand(@"
SELECT addition_mode,time_type,ends_at
FROM player_resource_additions
WHERE player_id=$1 AND resource_type=$2",c,t);
        q.Parameters.AddWithValue(playerId);
        q.Parameters.AddWithValue(resourceType);
        await using var r=await q.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))return new(0,1d,0,null);
        var mode=r.GetInt16(0);
        var timeType=r.GetInt16(1);
        var end=r.GetFieldValue<DateTimeOffset>(2);
        return end>DateTimeOffset.UtcNow?State(mode,timeType,end):new(0,1d,0,null);
    }

    static async Task<RequestRow?> ReadRequestAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,string requestKey,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand(@"
SELECT source,resource_type,addition_mode,time_type,charge_item_id,item_id,gold_spent,ends_at
FROM player_resource_addition_requests
WHERE player_id=$1 AND request_key=$2",c,t);
        q.Parameters.AddWithValue(playerId);
        q.Parameters.AddWithValue(requestKey);
        await using var r=await q.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))return null;
        return new(
            r.GetString(0),r.GetInt16(1),r.GetInt16(2),r.GetInt16(3),
            r.IsDBNull(4)?null:r.GetInt32(4),r.IsDBNull(5)?null:r.GetInt32(5),
            r.GetInt64(6),r.GetFieldValue<DateTimeOffset>(7));
    }

    static async Task UpsertAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,int resourceType,int additionMode,int timeType,DateTimeOffset endsAt,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand(@"
INSERT INTO player_resource_additions(player_id,resource_type,addition_mode,time_type,ends_at)
VALUES($1,$2,$3,$4,$5)
ON CONFLICT(player_id,resource_type) DO UPDATE
SET addition_mode=excluded.addition_mode,time_type=excluded.time_type,ends_at=excluded.ends_at",c,t);
        q.Parameters.AddWithValue(playerId);
        q.Parameters.AddWithValue((short)resourceType);
        q.Parameters.AddWithValue((short)additionMode);
        q.Parameters.AddWithValue((short)timeType);
        q.Parameters.AddWithValue(endsAt);
        await q.ExecuteNonQueryAsync(ct);
    }

    static async Task InsertRequestAsync(
        NpgsqlConnection c,NpgsqlTransaction t,long playerId,string requestKey,string source,int resourceType,int additionMode,int timeType,
        int? chargeItemId,int? itemId,long goldSpent,DateTimeOffset endsAt,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand(@"
INSERT INTO player_resource_addition_requests(
  player_id,request_key,source,resource_type,addition_mode,time_type,charge_item_id,item_id,gold_spent,ends_at)
VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)",c,t);
        q.Parameters.AddWithValue(playerId);
        q.Parameters.AddWithValue(requestKey);
        q.Parameters.AddWithValue(source);
        q.Parameters.AddWithValue((short)resourceType);
        q.Parameters.AddWithValue((short)additionMode);
        q.Parameters.AddWithValue((short)timeType);
        q.Parameters.AddWithValue((object?)chargeItemId??DBNull.Value);
        q.Parameters.AddWithValue((object?)itemId??DBNull.Value);
        q.Parameters.AddWithValue(goldSpent);
        q.Parameters.AddWithValue(endsAt);
        await q.ExecuteNonQueryAsync(ct);
    }

    static DateTimeOffset NextEnd(AdditionRow? existing,int additionMode,int timeType)
    {
        var now=DateTimeOffset.UtcNow;
        var start=existing is not null&&existing.EndsAt>now&&existing.AdditionMode==additionMode?existing.EndsAt:now;
        return start.AddDays(DurationDays(timeType));
    }

    static ResourceAdditionState State(int additionMode,int timeType,DateTimeOffset endsAt)
        =>new(additionMode,LegacyMultiplier(additionMode),Math.Max(0L,(long)(endsAt-DateTimeOffset.UtcNow).TotalMilliseconds),timeType);

    static (int ResourceType,int AdditionMode,int TimeType) ParseItemEffect(string effect)
    {
        var parts=effect.Split(';');
        if(parts.Length<3||!int.TryParse(parts[0],out var resourceType)||!int.TryParse(parts[1],out var additionMode)||!int.TryParse(parts[2],out var timeType))
            throw new GameException("RESOURCE_ADDITION_ITEM_EFFECT_INVALID","Legacy resource-token effect is invalid.",500);
        Validate(resourceType,additionMode,timeType);
        return(resourceType,additionMode,timeType);
    }

    static ChargeItemDefinition Charge(CanonicalContent content,int id)
        =>content.ChargeItems.TryGetValue(id,out var item)?item:throw new GameException("RESOURCE_ADDITION_STATIC_MISSING",$"Legacy chargeitem {id} is missing.",500);

    static int ChargeItemId(int resourceType,int additionMode)
    {
        Validate(resourceType,additionMode,1);
        return resourceType==RecruitResourceType?RecruitChargeItemIds[additionMode]:NormalChargeItemIds[additionMode];
    }

    static int RequiredResourceFunction(int resourceType)
    {
        if(resourceType is<1 or>5)throw new GameException("RESOURCE_ADDITION_REQUEST_INVALID","Resource type must be 1..5.");
        return ResourceOpenFunctions[resourceType];
    }

    static double Discount(CanonicalContent content,int timeType)=>timeType switch
    {
        1=>1d,
        2=>ConstantDouble(content,"Resource.Muti.Sale.Weekly"),
        3=>ConstantDouble(content,"Resource.Muti.Sale.Monthly"),
        _=>throw new GameException("RESOURCE_ADDITION_REQUEST_INVALID","Time type must be 1..3.")
    };

    static double ConstantDouble(CanonicalContent content,string key)
    {
        if(!content.Constants.TryGetValue(key,out var value)||!double.TryParse(value.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var number))
            throw new GameException("RESOURCE_ADDITION_STATIC_MISSING",$"Legacy constant {key} is missing.",500);
        return number;
    }

    static int DurationDays(int timeType)
    {
        if(timeType is<1 or>3)throw new GameException("RESOURCE_ADDITION_REQUEST_INVALID","Time type must be 1..3.");
        return DurationDaysByTimeType[timeType];
    }

    static double LegacyMultiplier(int additionMode)
    {
        if(additionMode is<1 or>3)throw new GameException("RESOURCE_ADDITION_MODE_INVALID",$"Legacy resource addition mode {additionMode} is invalid.");
        return AdditionRates[additionMode];
    }

    static void Validate(int resourceType,int additionMode,int timeType)
    {
        if(resourceType is<1 or>5)throw new GameException("RESOURCE_ADDITION_REQUEST_INVALID","Resource type must be 1..5.");
        _=LegacyMultiplier(additionMode);
        _=DurationDays(timeType);
    }

    static void ValidateRequestKey(string requestKey)
    {
        if(string.IsNullOrWhiteSpace(requestKey)||requestKey.Length>128)
            throw new GameException("RESOURCE_ADDITION_REQUEST_INVALID","Request key is required and must be at most 128 characters.");
    }
}

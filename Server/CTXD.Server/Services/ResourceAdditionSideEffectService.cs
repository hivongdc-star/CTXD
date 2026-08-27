using System.Text.Json.Serialization;
using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record ResourceAdditionRewardSideEffect(
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] int? RewardType,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] int? RewardValue);

public sealed record ResourceAdditionActivationResponse(
    ResourceAdditionState State,
    long GoldSpent,
    int? ItemId,
    bool Replayed,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] int? RewardType,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] int? RewardValue);

internal sealed class ResourceAdditionSideEffectService
{
    const int ResourceAdditionEventId=12;
    const int RecruitResourceType=5;
    const int RecruitRewardType=85;
    const int RecruitRewardItemId=1332;
    const int ResourceTokenItemType=13;

    sealed record PaidRequest(string Source,int ResourceType,int AdditionMode,int TimeType,DateTimeOffset CreatedAt);

    public async Task<ResourceAdditionRewardSideEffect> ApplyPaidRecruitRewardAsync(
        GameDb db,
        CanonicalContent content,
        IPlayerItemInventory inventory,
        long playerId,
        string requestKey,
        int additionMode,
        int timeType,
        CancellationToken ct)
    {
        // Legacy BuildingAction only calls ResourceAdditionEvent for type 5 mode 2/3,
        // and only for weekly/monthly purchases. Daily purchases have no event reward.
        if(additionMode is not(2 or 3)||timeType is not(2 or 3))return new(null,null);

        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);
        var request=await LockPaidRequestAsync(c,t,playerId,requestKey,ct);
        if(request.Source!="paid"||request.ResourceType!=RecruitResourceType||request.AdditionMode!=additionMode||request.TimeType!=timeType)
            throw new GameException("RESOURCE_ADDITION_SIDE_EFFECT_REQUEST_INVALID","Resource-addition reward request does not match the paid activation.",409);

        var prior=await ReadRewardAsync(c,t,playerId,requestKey,ct);
        if(prior is not null)
        {
            await t.CommitAsync(ct);
            return prior;
        }

        var activityId=await FindEventAtRequestTimeAsync(c,t,request.CreatedAt,ct);
        if(activityId is null)
        {
            await t.CommitAsync(ct);
            return new(null,null);
        }

        if(!content.Items.TryGetValue(RecruitRewardItemId,out var rewardItem)||rewardItem.Type!=ResourceTokenItemType)
            throw new GameException("RESOURCE_ADDITION_EVENT_STATIC_MISSING","Legacy ResourceAdditionEvent reward item 1332 is missing or has the wrong type.",500);

        // ResourceAdditionEvent.tokenMap: weekly selector 1 -> 1 token,
        // monthly selector 2 -> 6 tokens. Type 5 itemIdMap -> 1332, rewardTypeMap -> 85.
        var quantity=timeType==2?1:6;
        await inventory.GrantAsync(c,t,playerId,RecruitRewardItemId,rewardItem.Type,quantity,ct);

        await using(var save=new NpgsqlCommand(@"
INSERT INTO player_resource_addition_side_effects(
  player_id,request_key,event_activity_id,reward_type,reward_item_id,reward_quantity)
VALUES($1,$2,$3,$4,$5,$6)",c,t))
        {
            save.Parameters.AddWithValue(playerId);
            save.Parameters.AddWithValue(requestKey);
            save.Parameters.AddWithValue(activityId.Value);
            save.Parameters.AddWithValue((short)RecruitRewardType);
            save.Parameters.AddWithValue(RecruitRewardItemId);
            save.Parameters.AddWithValue(quantity);
            await save.ExecuteNonQueryAsync(ct);
        }

        await t.CommitAsync(ct);
        return new(RecruitRewardType,quantity);
    }

    static async Task<PaidRequest> LockPaidRequestAsync(
        NpgsqlConnection c,NpgsqlTransaction t,long playerId,string requestKey,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand(@"
SELECT source,resource_type,addition_mode,time_type,created_at
FROM player_resource_addition_requests
WHERE player_id=$1 AND request_key=$2
FOR UPDATE",c,t);
        q.Parameters.AddWithValue(playerId);
        q.Parameters.AddWithValue(requestKey);
        await using var r=await q.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))
            throw new GameException("RESOURCE_ADDITION_SIDE_EFFECT_REQUEST_MISSING","Paid resource-addition request is missing.",500);
        return new(r.GetString(0),r.GetInt16(1),r.GetInt16(2),r.GetInt16(3),r.GetFieldValue<DateTimeOffset>(4));
    }

    static async Task<ResourceAdditionRewardSideEffect?> ReadRewardAsync(
        NpgsqlConnection c,NpgsqlTransaction t,long playerId,string requestKey,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand(@"
SELECT reward_type,reward_quantity
FROM player_resource_addition_side_effects
WHERE player_id=$1 AND request_key=$2",c,t);
        q.Parameters.AddWithValue(playerId);
        q.Parameters.AddWithValue(requestKey);
        await using var r=await q.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)?new(r.GetInt16(0),r.GetInt32(1)):null;
    }

    static async Task<long?> FindEventAtRequestTimeAsync(
        NpgsqlConnection c,NpgsqlTransaction t,DateTimeOffset requestAt,CancellationToken ct)
    {
        // Legacy DefaultEvent.isEventTime is inclusive at both boundaries. Using the
        // persisted request timestamp makes retry deterministic even after event end.
        // created_at prevents a later back-dated schedule from retroactively granting a token.
        await using var q=new NpgsqlCommand(@"
SELECT id
FROM scheduled_activities
WHERE activity_type=$1
  AND created_at<=$2
  AND start_at<=$2
  AND end_at>=$2
  AND (expired_at IS NULL OR expired_at>=$2)
ORDER BY start_at DESC,id DESC
LIMIT 1",c,t);
        q.Parameters.AddWithValue(ResourceAdditionEventId);
        q.Parameters.AddWithValue(requestAt);
        var raw=await q.ExecuteScalarAsync(ct);
        return raw is null or DBNull?null:Convert.ToInt64(raw);
    }
}

using CTXD.Server.Data;

namespace CTXD.Server.Services;

public static class ResourceAdditionEndpoints
{
    static readonly ResourceAdditionService Service=new();
    static readonly ResourceAdditionSideEffectService SideEffects=new();

    public static IEndpointRouteBuilder MapResourceAdditionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/resource-additions/recruit",async(HttpRequest request,AuthService auth,GameDb db,CancellationToken ct)=>
        {
            var playerId=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(await Service.GetRecruitStateAsync(db,playerId,ct));
        });
        app.MapGet("/api/resource-additions/recruit/price",async(int additionMode,int timeType,HttpRequest request,AuthService auth,CanonicalContent content,CancellationToken ct)=>
        {
            _=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(Service.GetRecruitPrice(content,additionMode,timeType));
        });
        app.MapPost("/api/resource-additions/recruit/buy",async(ResourceAdditionBuyRequest body,HttpRequest request,AuthService auth,GameDb db,CanonicalContent content,IPlayerItemInventory inventory,QuestService quests,GamePushHub push,CancellationToken ct)=>
        {
            var playerId=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            var result=await Service.BuyRecruitAsync(db,content,playerId,body.AdditionMode,body.TimeType,body.RequestKey,ct);
            var reward=await SideEffects.ApplyPaidRecruitRewardAsync(db,content,inventory,playerId,body.RequestKey,body.AdditionMode,body.TimeType,ct);
            if(!result.Replayed)await push.SendAsync(playerId,"quest.updated",await quests.GetCurrentAsync(playerId,ct),ct);
            return Results.Ok(new ResourceAdditionActivationResponse(result.State,result.GoldSpent,result.ItemId,result.Replayed,reward.RewardType,reward.RewardValue));
        });
        app.MapPost("/api/resource-additions/recruit/use-item",async(ResourceAdditionUseItemRequest body,HttpRequest request,AuthService auth,GameDb db,CanonicalContent content,IPlayerItemInventory inventory,QuestService quests,GamePushHub push,CancellationToken ct)=>
        {
            var playerId=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            var result=await Service.UseRecruitItemAsync(db,content,inventory,playerId,body.ItemId,body.RequestKey,ct);
            if(!result.Replayed)await push.SendAsync(playerId,"quest.updated",await quests.GetCurrentAsync(playerId,ct),ct);
            return Results.Ok(result);
        });
        return app;
    }

    static string? Bearer(HttpRequest request)
    {
        var h=request.Headers["Authorization"].ToString();
        return h.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?h[7..].Trim():null;
    }
}

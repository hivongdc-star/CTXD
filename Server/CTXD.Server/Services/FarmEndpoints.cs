using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public static class FarmEndpoints
{
    public static IEndpointRouteBuilder MapFarmEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/world/farm",async(HttpRequest request,AuthService auth,GameDb db,CanonicalContent content,ResourceProductionService production,ExperienceService experience,IPlayerItemInventory items,DstqActivityService dstq,GamePushHub push,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(await Service(db,content,production,experience,items,dstq,push).GetAsync(player,ct));
        });
        app.MapPost("/api/world/farm/invest",async(HttpRequest request,AuthService auth,GameDb db,CanonicalContent content,ResourceProductionService production,ExperienceService experience,IPlayerItemInventory items,DstqActivityService dstq,GamePushHub push,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(await Service(db,content,production,experience,items,dstq,push).InvestAsync(player,ct));
        });
        app.MapPost("/api/world/farm/invest/recover",async(FarmGoldRequest body,HttpRequest request,AuthService auth,GameDb db,CanonicalContent content,ResourceProductionService production,ExperienceService experience,IPlayerItemInventory items,DstqActivityService dstq,GamePushHub push,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            var gold=await Service(db,content,production,experience,items,dstq,push).RecoverInvestAsync(player,body.RequestKey,ct);
            return Results.Ok(new{gold});
        });
        app.MapPost("/api/world/farm/start",async(FarmStartRequest body,HttpRequest request,AuthService auth,GameDb db,CanonicalContent content,ResourceProductionService production,ExperienceService experience,IPlayerItemInventory items,DstqActivityService dstq,GamePushHub push,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(await Service(db,content,production,experience,items,dstq,push).StartAsync(player,body.GeneralId,body.Type,ct));
        });
        app.MapGet("/api/world/farm/{generalId:int}/claim-cost",async(int generalId,HttpRequest request,AuthService auth,GameDb db,CanonicalContent content,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            await using var c=await db.DataSource.OpenConnectionAsync(ct);
            DateTimeOffset endsAt;
            await using(var q=new NpgsqlCommand("SELECT ends_at FROM player_farms WHERE player_id=$1 AND general_id=$2",c))
            {
                q.Parameters.AddWithValue(player);q.Parameters.AddWithValue(generalId);
                var value=await q.ExecuteScalarAsync(ct);
                if(value is null)throw new GameException("FARM_NOT_ACTIVE","General has no active Farm work.",409);
                endsAt=(DateTimeOffset)value;
            }
            var ms=Math.Max(0,(endsAt-DateTimeOffset.UtcNow).TotalMilliseconds);
            if(ms<=0)return Results.Ok(new{gold=0});
            if(!content.ChargeItems.TryGetValue(86,out var charge))throw new GameException("FARM_CHARGE_ITEM_MISSING","Legacy charge item 86 is missing.",500);
            var gold=(int)Math.Ceiling(ms/(charge.Param*60_000d))*charge.Cost;
            return Results.Ok(new{gold});
        });
        app.MapPost("/api/world/farm/{generalId:int}/stop",async(int generalId,HttpRequest request,AuthService auth,GameDb db,CanonicalContent content,ResourceProductionService production,ExperienceService experience,IPlayerItemInventory items,DstqActivityService dstq,GamePushHub push,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(await Service(db,content,production,experience,items,dstq,push).StopAsync(player,generalId,ct));
        });
        app.MapPost("/api/world/farm/{generalId:int}/claim",async(int generalId,FarmGoldRequest body,HttpRequest request,AuthService auth,GameDb db,CanonicalContent content,ResourceProductionService production,ExperienceService experience,IPlayerItemInventory items,DstqActivityService dstq,GamePushHub push,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(await Service(db,content,production,experience,items,dstq,push).ClaimAsync(player,generalId,body.RequestKey,ct));
        });
        app.MapPost("/api/world/farm/stop-all",async(HttpRequest request,AuthService auth,GameDb db,CanonicalContent content,ResourceProductionService production,ExperienceService experience,IPlayerItemInventory items,DstqActivityService dstq,GamePushHub push,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(new{items=await Service(db,content,production,experience,items,dstq,push).StopAllAsync(player,ct)});
        });
        return app;
    }

    static FarmService Service(GameDb db,CanonicalContent content,ResourceProductionService production,ExperienceService experience,IPlayerItemInventory items,DstqActivityService dstq,GamePushHub push)=>new(db,content,production,experience,items,dstq,push);
    static string? Bearer(HttpRequest request){var h=request.Headers["Authorization"].ToString();return h.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?h[7..].Trim():null;}
}

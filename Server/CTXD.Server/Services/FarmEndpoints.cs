using CTXD.Server.Data;

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

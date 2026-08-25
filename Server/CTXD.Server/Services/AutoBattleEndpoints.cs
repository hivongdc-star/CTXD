using CTXD.Server.Data;

namespace CTXD.Server.Services;

public static class AutoBattleEndpoints
{
    public static IEndpointRouteBuilder MapAutoBattleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/world/auto-battle",async(
            HttpRequest request,AuthService auth,GameDb db,CanonicalContent content,TechnologyEffectService technologies,
            ResourceProductionService production,WorldService world,BattleService battles,CancellationToken ct)=>
        {
            var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            var service=new AutoBattleService(db,content,technologies,production,world,battles);
            return Results.Ok(await service.GetAsync(id,ct));
        });

        app.MapPost("/api/world/auto-battle/start",async(
            AutoBattleStartRequest body,HttpRequest request,AuthService auth,GameDb db,CanonicalContent content,TechnologyEffectService technologies,
            ResourceProductionService production,WorldService world,BattleService battles,GamePushHub push,CancellationToken ct)=>
        {
            var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            var service=new AutoBattleService(db,content,technologies,production,world,battles);
            var state=await service.StartAsync(id,body.CityId,ct);
            await push.SendAsync(id,"auto-battle.updated",state,ct);
            return Results.Ok(state);
        });

        app.MapPost("/api/world/auto-battle/stop",async(
            HttpRequest request,AuthService auth,GameDb db,CanonicalContent content,TechnologyEffectService technologies,
            ResourceProductionService production,WorldService world,BattleService battles,GamePushHub push,CancellationToken ct)=>
        {
            var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            var service=new AutoBattleService(db,content,technologies,production,world,battles);
            var state=await service.StopAsync(id,ct);
            await push.SendAsync(id,"auto-battle.updated",state,ct);
            return Results.Ok(state);
        });
        return app;
    }

    static string? Bearer(HttpRequest request)
    {
        var h=request.Headers["Authorization"].ToString();
        return h.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?h[7..].Trim():null;
    }
}

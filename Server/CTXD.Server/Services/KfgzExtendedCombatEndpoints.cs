using CTXD.Server.Data;

namespace CTXD.Server.Services;

public static class KfgzExtendedCombatEndpoints
{
    public static IEndpointRouteBuilder MapKfgzExtendedCombat(this IEndpointRouteBuilder app)
    {
        KfgzMubingWorker.Start(app.ServiceProvider);
        AutoBattleWorker.Start(app.ServiceProvider);
        MineWorker.Start(app.ServiceProvider);
        PrisonWorker.Start(app.ServiceProvider);
        app.MapRankEndpoints();
        app.MapAutoBattleEndpoints();
        app.MapFarmEndpoints();
        app.MapMineEndpoints();
        app.MapTreasureEndpoints();
        app.MapWeaponEndpoints();
        app.MapTicketsMarketEndpoints();
        app.MapPrisonEndpoints();

        app.MapGet("/api/kfgz/resources",async(HttpRequest request,AuthService auth,KfgzExtendedCombatService combat,GameDb db,CanonicalContent content,ResourceProductionService production,TechnologyEffectService technologies,GamePushHub push,CancellationToken ct)=>
        {
            var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            var mubing=new KfgzMubingService(db,content,production,technologies,push);
            await mubing.TickPlayerAsync(id,ct);
            return Results.Ok(await combat.ResourcesAsync(id,ct));
        });

        app.MapPost("/api/kfgz/generals/{generalId:int}/mubing",async(int generalId,HttpRequest request,AuthService auth,GameDb db,CanonicalContent content,ResourceProductionService production,TechnologyEffectService technologies,GamePushHub push,CancellationToken ct)=>
        {
            var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            var mubing=new KfgzMubingService(db,content,production,technologies,push);
            return Results.Ok(await mubing.StartAsync(id,generalId,ct));
        });

        app.MapPost("/api/kfgz/generals/{generalId:int}/fast-recruit",async(int generalId,HttpRequest request,AuthService auth,GameDb db,CanonicalContent content,ResourceProductionService production,TechnologyEffectService technologies,DstqActivityService dstq,GamePushHub push,CancellationToken ct)=>
        {
            var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            var fast=new KfgzFastRecruitService(db,content,production,technologies,dstq,push);
            return Results.Ok(await fast.FastRecruitAsync(id,generalId,ct));
        });

        app.MapGet("/api/kfgz/world/{cityId:int}/call-generals",async(int cityId,HttpRequest request,AuthService auth,CanonicalContent content,KfgzService kfgz,GameDb db,TechnologyEffectService technologies,BattleService battles,GamePushHub push,CancellationToken ct)=>
        {
            var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            var reinforcement=new KfgzReinforcementService(db,content,technologies,battles,push);
            var call=new KfgzCallGeneralService(content,kfgz,reinforcement);
            return Results.Ok(await call.InfoAsync(id,cityId,ct));
        });

        app.MapPost("/api/kfgz/world/{cityId:int}/call-generals",async(int cityId,KfgzCallGeneralRequest body,HttpRequest request,AuthService auth,CanonicalContent content,KfgzService kfgz,GameDb db,TechnologyEffectService technologies,BattleService battles,GamePushHub push,CancellationToken ct)=>
        {
            var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            var reinforcement=new KfgzReinforcementService(db,content,technologies,battles,push);
            var call=new KfgzCallGeneralService(content,kfgz,reinforcement);
            return Results.Ok(await call.CallAsync(id,cityId,body,ct));
        });

        app.MapPost("/api/kfgz/battles/{battleId:long}/reinforce",async(long battleId,KfgzReinforcementRequest body,HttpRequest request,AuthService auth,GameDb db,CanonicalContent content,TechnologyEffectService technologies,BattleService battles,GamePushHub push,CancellationToken ct)=>
        {
            var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            var reinforcement=new KfgzReinforcementService(db,content,technologies,battles,push);
            return Results.Ok(await reinforcement.ReinforceAsync(id,battleId,body,ct));
        });

        app.MapPost("/api/battles/{battleId:long}/phantom",async(long battleId,KfgzPhantomRequest body,HttpRequest request,AuthService auth,KfgzExtendedCombatService combat,CancellationToken ct)=>
        {
            var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(await combat.CreatePhantomAsync(id,battleId,body,ct));
        });

        app.MapPost("/api/battles/{battleId:long}/rush",async(long battleId,KfgzRushRequest body,HttpRequest request,AuthService auth,KfgzRushService rush,CancellationToken ct)=>
        {
            var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(await rush.RushAsync(id,battleId,body,ct));
        });
        return app;
    }

    static string? Bearer(HttpRequest request)
    {
        var h=request.Headers["Authorization"].ToString();
        return h.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?h[7..].Trim():null;
    }
}

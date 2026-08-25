using CTXD.Server.Data;

namespace CTXD.Server.Services;

public static class KfgzExtendedCombatEndpoints
{
    public static IEndpointRouteBuilder MapKfgzExtendedCombat(this IEndpointRouteBuilder app)
    {
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

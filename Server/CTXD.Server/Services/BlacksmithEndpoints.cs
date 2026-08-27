using CTXD.Server.Data;

namespace CTXD.Server.Services;

public static class BlacksmithEndpoints
{
    public static IEndpointRouteBuilder MapBlacksmithEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/blacksmith",async(HttpRequest request,AuthService auth,GameDb db,IPlayerItemInventory items,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(await new BlacksmithService(db,items).GetAsync(player,ct));
        });

        app.MapPost("/api/blacksmith/smiths/1/unlock",async(HttpRequest request,AuthService auth,GameDb db,IPlayerItemInventory items,GamePushHub push,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            var service=new BlacksmithService(db,items);
            var result=await service.UnlockSmith1Async(player,ct);
            await push.SendAsync(player,"blacksmith.updated",await service.GetAsync(player,ct),ct);
            return Results.Ok(result);
        });

        app.MapPost("/api/blacksmith/smiths/1/dissolve",async(HttpRequest request,AuthService auth,GameDb db,IPlayerItemInventory items,GamePushHub push,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            var service=new BlacksmithService(db,items);
            var result=await service.DissolveSmith1Async(player,ct);
            await push.SendAsync(player,"blacksmith.updated",await service.GetAsync(player,ct),ct);
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

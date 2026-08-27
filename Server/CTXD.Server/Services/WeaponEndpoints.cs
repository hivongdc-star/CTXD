using CTXD.Server.Data;

namespace CTXD.Server.Services;

public static class WeaponEndpoints
{
    public static IEndpointRouteBuilder MapWeaponEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/weapons",async(HttpRequest request,AuthService auth,GameDb db,CanonicalContent content,IPlayerItemInventory items,ResourceProductionService production,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(await new WeaponService(db,content,items,production).GetAsync(player,ct));
        });
        app.MapPost("/api/weapons/{weaponId:int}/upgrade",async(int weaponId,HttpRequest request,AuthService auth,GameDb db,CanonicalContent content,IPlayerItemInventory items,ResourceProductionService production,GamePushHub push,CancellationToken ct)=>
        {
            var player=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            var result=await new WeaponService(db,content,items,production).UpgradeAsync(player,weaponId,ct);
            await push.SendAsync(player,"weapon.updated",result,ct);
            return Results.Ok(result);
        });
        return app;
    }
    static string? Bearer(HttpRequest request){var h=request.Headers["Authorization"].ToString();return h.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?h[7..].Trim():null;}
}

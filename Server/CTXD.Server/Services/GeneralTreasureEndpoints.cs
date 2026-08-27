namespace CTXD.Server.Services;

public static class GeneralTreasureEndpoints
{
    public static IEndpointRouteBuilder MapGeneralTreasureEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/general-treasures",async(HttpRequest request,AuthService auth,GeneralTreasureService treasures,CancellationToken ct)=>
        {
            var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(new{items=await treasures.GetAsync(id,ct)});
        });
        app.MapPost("/api/general-treasures/{instanceId:long}/equip",async(long instanceId,GeneralTreasureEquipRequest body,HttpRequest request,AuthService auth,GeneralTreasureService treasures,CancellationToken ct)=>
        {
            var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await treasures.EquipAsync(id,instanceId,body.GeneralId,ct));
        });
        app.MapPost("/api/general-treasures/{instanceId:long}/unequip",async(long instanceId,HttpRequest request,AuthService auth,GeneralTreasureService treasures,CancellationToken ct)=>
        {
            var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await treasures.UnequipAsync(id,instanceId,ct));
        });
        return app;
    }

    static string? Bearer(HttpRequest request){var h=request.Headers["Authorization"].ToString();return h.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?h[7..].Trim():null;}
}

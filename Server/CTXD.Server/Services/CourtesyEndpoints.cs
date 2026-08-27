using CTXD.Server.Data;

namespace CTXD.Server.Services;

public static class CourtesyEndpoints
{
    public static void MapCourtesy(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        app.MapGet("/api/courtesy",async(HttpRequest request,AuthService auth,GameDb db,CancellationToken ct)=>
        {
            var playerId=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(await CourtesyService.GetAsync(db,playerId,ct));
        });
        app.MapPost("/api/courtesy/events/{eventId:long}/handle",async(long eventId,HttpRequest request,AuthService auth,GameDb db,CancellationToken ct)=>
        {
            var playerId=await auth.ResolvePlayerIdAsync(Bearer(request),ct);
            return Results.Ok(await CourtesyService.HandleAsync(db,playerId,eventId,ct));
        });
    }

    static string? Bearer(HttpRequest request)
    {
        var h=request.Headers["Authorization"].ToString();
        return h.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?h[7..].Trim():null;
    }
}

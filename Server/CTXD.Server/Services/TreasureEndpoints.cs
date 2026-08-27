namespace CTXD.Server.Services;
public static class TreasureEndpoints
{
    public static IEndpointRouteBuilder MapTreasureEndpoints(this IEndpointRouteBuilder app){app.MapGet("/api/treasures",async(HttpRequest request,AuthService auth,TreasureService treasures,CancellationToken ct)=>Results.Ok(await treasures.GetAsync(await auth.ResolvePlayerIdAsync(Bearer(request),ct),ct)));return app;}
    static string? Bearer(HttpRequest request){var h=request.Headers["Authorization"].ToString();return h.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?h[7..].Trim():null;}
}

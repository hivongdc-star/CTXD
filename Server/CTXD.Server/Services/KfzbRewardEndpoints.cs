namespace CTXD.Server.Services;

public static class KfzbRewardEndpoints
{
    public static IEndpointRouteBuilder MapKfzbRewardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/kfzb/reward",async(HttpRequest request,AuthService auth,KfzbService kfzb,CancellationToken ct)=>
        {
            var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await kfzb.RewardAsync(id,ct));
        });
        app.MapPost("/api/kfzb/reward/claim",async(HttpRequest request,AuthService auth,KfzbService kfzb,CancellationToken ct)=>
        {
            var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await kfzb.ClaimRewardAsync(id,ct));
        });
        return app;
    }

    static string? Bearer(HttpRequest request){var h=request.Headers["Authorization"].ToString();return h.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?h[7..].Trim():null;}
}

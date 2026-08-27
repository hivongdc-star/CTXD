namespace CTXD.Server.Services;

public static class KfwdRewardEndpoints
{
    public static WebApplication MapKfwdRewards(this WebApplication app)
    {
        app.MapGet("/api/kfwd/rewards", async (
            HttpRequest request,
            AuthService auth,
            KfwdRewardService rewards,
            CancellationToken ct) =>
        {
            var playerId = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
            return Results.Ok(await rewards.GetAsync(playerId, ct));
        });

        app.MapPost("/api/kfwd/treasure/claim", async (
            HttpRequest request,
            AuthService auth,
            KfwdRewardService rewards,
            CancellationToken ct) =>
        {
            var playerId = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
            return Results.Ok(await rewards.ClaimTreasureAsync(playerId, ct));
        });

        return app;
    }

    static string? Bearer(HttpRequest request)
    {
        var header = request.Headers["Authorization"].ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header[7..].Trim()
            : null;
    }
}

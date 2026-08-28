namespace CTXD.Server.Services;

public static class BlacksmithEndpoints
{
    public static void MapBlacksmith(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        app.MapGet("/api/blacksmith", async (
            HttpRequest request,
            AuthService auth,
            BlacksmithService blacksmith,
            CancellationToken ct) =>
        {
            var playerId = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
            return Results.Ok(await blacksmith.GetAsync(playerId, ct));
        });

        app.MapPost("/api/blacksmith/smiths/1/unlock", async (
            HttpRequest request,
            AuthService auth,
            BlacksmithService blacksmith,
            CancellationToken ct) =>
        {
            var playerId = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
            return Results.Ok(await blacksmith.UnlockAsync(playerId, ct));
        });

        app.MapPost("/api/blacksmith/smiths/1/dissolve", async (
            HttpRequest request,
            AuthService auth,
            BlacksmithService blacksmith,
            CancellationToken ct) =>
        {
            var playerId = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
            return Results.Ok(await blacksmith.DissolveAsync(playerId, ct));
        });
    }

    static string? Bearer(HttpRequest request)
    {
        var header = request.Headers["Authorization"].ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header[7..].Trim()
            : null;
    }
}

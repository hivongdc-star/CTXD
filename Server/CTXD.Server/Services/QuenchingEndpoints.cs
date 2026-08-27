namespace CTXD.Server.Services;

public static class QuenchingEndpoints
{
    public static Microsoft.AspNetCore.Builder.WebApplication MapQuenching(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        app.MapGet("/api/equipment/inventory/{instanceId:long}/quenching", async (
            long instanceId,
            HttpRequest request,
            AuthService auth,
            QuenchingService quenching,
            CancellationToken ct) =>
        {
            var playerId = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
            return Results.Ok(await quenching.GetAsync(playerId, instanceId, ct));
        });

        app.MapPost("/api/equipment/inventory/{instanceId:long}/quenching/paid", async (
            long instanceId,
            HttpRequest request,
            AuthService auth,
            QuenchingService quenching,
            GamePushHub push,
            CancellationToken ct) =>
        {
            var playerId = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
            var result = await quenching.PaidAsync(playerId, instanceId, ct);
            await push.SendAsync(playerId, "equipment.quenching.updated", result, ct);
            return Results.Ok(result);
        });

        app.MapPost("/api/equipment/inventory/{instanceId:long}/quenching/free", async (
            long instanceId,
            HttpRequest request,
            AuthService auth,
            QuenchingService quenching,
            GamePushHub push,
            CancellationToken ct) =>
        {
            var playerId = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
            var result = await quenching.FreeAsync(playerId, instanceId, ct);
            await push.SendAsync(playerId, "equipment.quenching.updated", result, ct);
            return Results.Ok(result);
        });

        return app.MapEquipmentComposites();
    }

    static string? Bearer(HttpRequest request)
    {
        var header = request.Headers["Authorization"].ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header[7..].Trim()
            : null;
    }
}

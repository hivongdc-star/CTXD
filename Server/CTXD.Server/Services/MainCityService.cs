using CTXD.Server.Data;
using CTXD.Server.Models;

namespace CTXD.Server.Services;

public sealed class MainCityService(
    GameDb db,
    PlayerQueryService players,
    ResourceProductionService resources,
    BuildingService buildings,
    TutorialService tutorial)
{
    public async Task<MainCityResponse> GetAsync(long playerId, CancellationToken ct)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await buildings.CompleteDueAsync(conn, tx, playerId, ct);
        var resource = await resources.AccrueAndGetAsync(playerId, ct, conn, tx);
        await tutorial.TryCompleteAsync(conn, tx, playerId, "building_output", [1, resource.CopperPerHour], ct);
        await tutorial.TryCompleteAsync(conn, tx, playerId, "building_output", [2, resource.WoodPerHour], ct);
        await tutorial.TryCompleteAsync(conn, tx, playerId, "building_output", [3, resource.FoodPerHour], ct);
        await tutorial.TryCompleteAsync(conn, tx, playerId, "building_output", [4, resource.IronPerHour], ct);
        var player = await players.GetPlayerAsync(playerId, ct, conn, tx);
        var buildingViews = await buildings.GetViewsAsync(conn, tx, playerId, ct);
        await tx.CommitAsync(ct);
        return new MainCityResponse(player, resource, buildingViews);
    }
}

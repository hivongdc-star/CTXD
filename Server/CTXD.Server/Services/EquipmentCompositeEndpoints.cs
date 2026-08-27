using CTXD.Server.Models;

namespace CTXD.Server.Services;

public static class EquipmentCompositeEndpoints
{
    public static Microsoft.AspNetCore.Builder.WebApplication MapEquipmentComposites(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        app.MapGet("/api/equipment/composites", async (HttpRequest request, AuthService auth, GameDb db, CanonicalContent content, CancellationToken ct) =>
        {
            var playerId = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
            return Results.Ok(await EquipmentCompositeService.GetAsync(db, content, playerId, ct));
        });

        app.MapPost("/api/equipment/composites/suits/{itemId:int}/compound", async (int itemId, HttpRequest request, AuthService auth, GameDb db, CanonicalContent content, TechnologyEffectService technology, IPlayerItemInventory items, EquipmentInventoryService inventory, GamePushHub push, CancellationToken ct) =>
        {
            var playerId = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
            var result = await EquipmentCompositeService.CompoundSuitAsync(db, content, technology, items, playerId, itemId, ct);
            await PushEquipmentAsync(playerId, result, inventory, push, ct);
            return Results.Ok(result);
        });

        app.MapPost("/api/equipment/composites/prosets/{itemId:int}/compound", async (int itemId, HttpRequest request, AuthService auth, GameDb db, CanonicalContent content, TechnologyEffectService technology, IPlayerItemInventory items, EquipmentInventoryService inventory, GamePushHub push, CancellationToken ct) =>
        {
            var playerId = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
            var result = await EquipmentCompositeService.CompoundProsetAsync(db, content, technology, items, playerId, itemId, ct);
            await PushEquipmentAsync(playerId, result, inventory, push, ct);
            return Results.Ok(result);
        });

        app.MapPost("/api/equipment/composites/{instanceId:long}/demount", async (long instanceId, HttpRequest request, AuthService auth, GameDb db, CanonicalContent content, TechnologyEffectService technology, IPlayerItemInventory items, EquipmentInventoryService inventory, GamePushHub push, CancellationToken ct) =>
        {
            var playerId = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
            var result = await EquipmentCompositeService.DemountAsync(db, content, technology, items, playerId, instanceId, ct);
            await PushEquipmentAsync(playerId, result, inventory, push, ct);
            return Results.Ok(result);
        });

        app.MapPost("/api/equipment/composites/{instanceId:long}/equip", async (long instanceId, EquipRequest body, HttpRequest request, AuthService auth, GameDb db, CanonicalContent content, EquipmentInventoryService inventory, GamePushHub push, CancellationToken ct) =>
        {
            var playerId = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
            var result = await EquipmentCompositeService.EquipAsync(db, content, playerId, instanceId, body.GeneralId, ct);
            await PushEquipmentAsync(playerId, result, inventory, push, ct);
            return Results.Ok(result);
        });

        app.MapPost("/api/equipment/composites/{instanceId:long}/unequip", async (long instanceId, HttpRequest request, AuthService auth, GameDb db, CanonicalContent content, EquipmentInventoryService inventory, GamePushHub push, CancellationToken ct) =>
        {
            var playerId = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
            var result = await EquipmentCompositeService.UnequipAsync(db, content, playerId, instanceId, ct);
            await PushEquipmentAsync(playerId, result, inventory, push, ct);
            return Results.Ok(result);
        });

        return app;
    }

    static async Task PushEquipmentAsync(long playerId, EquipmentCompositeInventoryView result, EquipmentInventoryService inventory, GamePushHub push, CancellationToken ct)
    {
        await push.SendAsync(playerId, "equipment.composites.updated", result, ct);
        await push.SendAsync(playerId, "equipment.inventory.updated", await inventory.GetAsync(playerId, ct), ct);
    }

    static string? Bearer(HttpRequest request)
    {
        var header = request.Headers["Authorization"].ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
    }
}

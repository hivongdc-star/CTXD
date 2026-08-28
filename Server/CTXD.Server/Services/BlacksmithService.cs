using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record BlacksmithSmithView(
    int SmithId,
    bool Unlocked,
    int Level,
    int DailyUsed,
    int DailyLimit,
    int BlueprintItemId,
    int BlueprintItemType,
    int BlueprintCount,
    int StoneItemId,
    int StoneItemType,
    int StoneCount,
    long IronPerDissolve);

public sealed record BlacksmithView(
    bool FunctionOpen,
    int PlayerLevel,
    long Iron,
    BlacksmithSmithView Smith1);

public sealed class BlacksmithService(GameDb db, IPlayerItemInventory inventory)
{
    const int FunctionId = 66;
    const int SmithId = 1;
    const int MinUnlockLevel = 100;
    const int BlueprintItemId = 1201;
    const int BlueprintItemType = 15;
    const int BlueprintCost = 1;
    const int StoneItemId = 1401;
    const int StoneItemType = 16;
    const int StoneCost = 1;
    const int DailyLimit = 5;

    public async Task<BlacksmithView> GetAsync(long playerId, CancellationToken ct)
    {
        await using var connection = await db.DataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await ResetDailyUsageAsync(connection, transaction, playerId, ct);
        var view = await SnapshotAsync(connection, transaction, playerId, ct);
        await transaction.CommitAsync(ct);
        return view;
    }

    public async Task<BlacksmithView> UnlockAsync(long playerId, CancellationToken ct)
    {
        await using var connection = await db.DataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var playerLevel = await LockPlayerAsync(connection, transaction, playerId, ct);
        await RequireFunctionAsync(connection, transaction, playerId, ct);

        await ResetDailyUsageAsync(connection, transaction, playerId, ct);
        if (await SmithExistsAsync(connection, transaction, playerId, ct))
        {
            var existing = await SnapshotAsync(connection, transaction, playerId, ct);
            await transaction.CommitAsync(ct);
            return existing;
        }

        if (playerLevel < MinUnlockLevel)
            throw new GameException("BLACKSMITH_LEVEL_REQUIRED", $"Blacksmith 1 requires player level {MinUnlockLevel}.");

        if (!await inventory.ConsumeAsync(connection, transaction, playerId, BlueprintItemId, BlueprintItemType, BlueprintCost, ct))
            throw new GameException("BLACKSMITH_BLUEPRINT_NOT_ENOUGH", "Not enough legacy Blacksmith blueprint item 1201/type 15.");

        await using (var insert = new NpgsqlCommand(
            "INSERT INTO player_blacksmith(player_id,smith_id,level,daily_used,usage_date) VALUES($1,$2,1,0,CURRENT_DATE)",
            connection, transaction))
        {
            insert.Parameters.AddWithValue(playerId);
            insert.Parameters.AddWithValue(SmithId);
            await insert.ExecuteNonQueryAsync(ct);
        }

        var view = await SnapshotAsync(connection, transaction, playerId, ct);
        await transaction.CommitAsync(ct);
        return view;
    }

    public async Task<BlacksmithView> DissolveAsync(long playerId, CancellationToken ct)
    {
        await using var connection = await db.DataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        _ = await LockPlayerAsync(connection, transaction, playerId, ct);
        await RequireFunctionAsync(connection, transaction, playerId, ct);
        await ResetDailyUsageAsync(connection, transaction, playerId, ct);

        int level;
        int dailyUsed;
        await using (var smith = new NpgsqlCommand(
            "SELECT level,daily_used FROM player_blacksmith WHERE player_id=$1 AND smith_id=$2 FOR UPDATE",
            connection, transaction))
        {
            smith.Parameters.AddWithValue(playerId);
            smith.Parameters.AddWithValue(SmithId);
            await using var reader = await smith.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new GameException("BLACKSMITH_NOT_UNLOCKED", "Blacksmith 1 is not unlocked.");
            level = reader.GetInt32(0);
            dailyUsed = reader.GetInt32(1);
        }

        if (dailyUsed >= DailyLimit)
            throw new GameException("BLACKSMITH_DAILY_LIMIT", "Blacksmith 1 has reached the legacy daily dissolve limit.");

        var ironOutput = IronOutput(level);
        if (!await inventory.ConsumeAsync(connection, transaction, playerId, StoneItemId, StoneItemType, StoneCost, ct))
            throw new GameException("BLACKSMITH_STONE_NOT_ENOUGH", "Not enough legacy Blacksmith dissolve item 1401/type 16.");

        await using (var resource = new NpgsqlCommand(
            "UPDATE player_resources SET iron=iron+$2,updated_at=now() WHERE player_id=$1",
            connection, transaction))
        {
            resource.Parameters.AddWithValue(playerId);
            resource.Parameters.AddWithValue(ironOutput);
            if (await resource.ExecuteNonQueryAsync(ct) != 1)
                throw new GameException("BLACKSMITH_PLAYER_STATE_MISSING", "Player resource state is missing.", 500);
        }

        await using (var update = new NpgsqlCommand(
            "UPDATE player_blacksmith SET daily_used=daily_used+1,usage_date=CURRENT_DATE,updated_at=now() WHERE player_id=$1 AND smith_id=$2",
            connection, transaction))
        {
            update.Parameters.AddWithValue(playerId);
            update.Parameters.AddWithValue(SmithId);
            await update.ExecuteNonQueryAsync(ct);
        }

        var view = await SnapshotAsync(connection, transaction, playerId, ct);
        await transaction.CommitAsync(ct);
        return view;
    }

    static long IronOutput(int level) => level switch
    {
        1 => 4000,
        2 => 6400,
        3 => 10000,
        _ => throw new GameException("BLACKSMITH_STATIC_MISSING", $"Authoritative hm_bs_gold mapping is missing for Blacksmith level {level}.", 500)
    };

    static async Task<int> LockPlayerAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long playerId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT level FROM players WHERE id=$1 FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue(playerId);
        var value = await command.ExecuteScalarAsync(ct);
        if (value is null)
            throw new GameException("BLACKSMITH_PLAYER_STATE_MISSING", "Player state is missing.", 500);
        return Convert.ToInt32(value);
    }

    static async Task RequireFunctionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long playerId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM player_functions WHERE player_id=$1 AND function_id=$2",
            connection, transaction);
        command.Parameters.AddWithValue(playerId);
        command.Parameters.AddWithValue(FunctionId);
        if (await command.ExecuteScalarAsync(ct) is null)
            throw new GameException("BLACKSMITH_LOCKED", "Legacy Blacksmith function 66 is not open.", 403);
    }

    static async Task ResetDailyUsageAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long playerId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE player_blacksmith SET daily_used=0,usage_date=CURRENT_DATE,updated_at=now() WHERE player_id=$1 AND smith_id=$2 AND usage_date<>CURRENT_DATE",
            connection, transaction);
        command.Parameters.AddWithValue(playerId);
        command.Parameters.AddWithValue(SmithId);
        await command.ExecuteNonQueryAsync(ct);
    }

    static async Task<bool> SmithExistsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long playerId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM player_blacksmith WHERE player_id=$1 AND smith_id=$2 FOR UPDATE",
            connection, transaction);
        command.Parameters.AddWithValue(playerId);
        command.Parameters.AddWithValue(SmithId);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    static async Task<BlacksmithView> SnapshotAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long playerId, CancellationToken ct)
    {
        int playerLevel;
        long iron;
        bool functionOpen;
        await using (var player = new NpgsqlCommand(
            "SELECT p.level,r.iron,EXISTS(SELECT 1 FROM player_functions f WHERE f.player_id=p.id AND f.function_id=$2) FROM players p JOIN player_resources r ON r.player_id=p.id WHERE p.id=$1",
            connection, transaction))
        {
            player.Parameters.AddWithValue(playerId);
            player.Parameters.AddWithValue(FunctionId);
            await using var reader = await player.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new GameException("BLACKSMITH_PLAYER_STATE_MISSING", "Player or resource state is missing.", 500);
            playerLevel = reader.GetInt32(0);
            iron = reader.GetInt64(1);
            functionOpen = reader.GetBoolean(2);
        }

        int level = 0;
        int dailyUsed = 0;
        var unlocked = false;
        await using (var smith = new NpgsqlCommand(
            "SELECT level,daily_used FROM player_blacksmith WHERE player_id=$1 AND smith_id=$2",
            connection, transaction))
        {
            smith.Parameters.AddWithValue(playerId);
            smith.Parameters.AddWithValue(SmithId);
            await using var reader = await smith.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                unlocked = true;
                level = reader.GetInt32(0);
                dailyUsed = reader.GetInt32(1);
            }
        }

        long blueprintCount;
        long stoneCount;
        await using (var items = new NpgsqlCommand(
            "SELECT COALESCE((SELECT SUM(quantity)::bigint FROM player_items WHERE player_id=$1 AND item_id=$2 AND item_type=$3),0),COALESCE((SELECT SUM(quantity)::bigint FROM player_items WHERE player_id=$1 AND item_id=$4 AND item_type=$5),0)",
            connection, transaction))
        {
            items.Parameters.AddWithValue(playerId);
            items.Parameters.AddWithValue(BlueprintItemId);
            items.Parameters.AddWithValue(BlueprintItemType);
            items.Parameters.AddWithValue(StoneItemId);
            items.Parameters.AddWithValue(StoneItemType);
            await using var reader = await items.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            blueprintCount = reader.GetInt64(0);
            stoneCount = reader.GetInt64(1);
        }

        var displayOutput = unlocked ? IronOutput(level) : IronOutput(1);
        return new BlacksmithView(
            functionOpen,
            playerLevel,
            iron,
            new BlacksmithSmithView(
                SmithId,
                unlocked,
                level,
                dailyUsed,
                DailyLimit,
                BlueprintItemId,
                BlueprintItemType,
                checked((int)blueprintCount),
                StoneItemId,
                StoneItemType,
                checked((int)stoneCount),
                displayOutput));
    }
}

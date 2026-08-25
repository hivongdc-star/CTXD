using CTXD.Server.Data;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace CTXD.Server.Services;

public sealed class BuildingCompletionWorker(
    GameDb db,
    BuildingService buildings,
    MainCityService mainCity,
    GamePushHub push,
    ILogger<BuildingCompletionWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var players = await FindDuePlayersAsync(stoppingToken);
                foreach (var playerId in players)
                {
                    var completed = await CompleteAsync(playerId, stoppingToken);
                    if (completed <= 0) continue;
                    var state = await mainCity.GetAsync(playerId, stoppingToken);
                    await push.SendAsync(playerId, "maincity.updated", state, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                log.LogError(ex, "Building completion loop failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    async Task<long[]> FindDuePlayersAsync(CancellationToken ct)
    {
        var list = new List<long>();
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT DISTINCT player_id FROM player_buildings WHERE state=1 AND upgrade_complete_at<=now() LIMIT 200", conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(r.GetInt64(0));
        return list.ToArray();
    }

    async Task<int> CompleteAsync(long playerId, CancellationToken ct)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var count = await buildings.CompleteDueAsync(conn, tx, playerId, ct);
        await tx.CommitAsync(ct);
        return count;
    }
}

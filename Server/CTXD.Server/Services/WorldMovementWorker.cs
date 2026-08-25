using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed class WorldMovementWorker(
    GameDb db,
    WorldService world,
    GamePushHub push,
    ILogger<WorldMovementWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                foreach (var playerId in await FindDuePlayersAsync(stoppingToken))
                {
                    var state = await world.GetAsync(playerId, stoppingToken);
                    await push.SendAsync(playerId, "world.updated", state, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { log.LogError(ex, "World movement completion tick failed"); }
        }
    }

    async Task<long[]> FindDuePlayersAsync(CancellationToken ct)
    {
        var players = new List<long>();
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT DISTINCT player_id FROM player_world_moves WHERE arrives_at<=now() LIMIT 200", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) players.Add(reader.GetInt64(0));
        return players.ToArray();
    }
}

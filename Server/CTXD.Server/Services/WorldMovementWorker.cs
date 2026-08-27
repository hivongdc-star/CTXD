using System.Collections.Concurrent;
using System.Text.Json;
using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed class WorldMovementWorker(
    GameDb db,
    CanonicalContent content,
    TechnologyEffectService technologies,
    ResourceProductionService production,
    ExperienceService experience,
    IPlayerItemInventory items,
    DstqActivityService dstq,
    WorldService world,
    BattleService battles,
    GamePushHub push,
    ILogger<WorldMovementWorker> log) : BackgroundService
{
    readonly AutoBattleService autoBattle=new(db,content,technologies,production,world,battles);
    readonly FarmArrivalService farmArrivals=new(db,content,production,experience,items,dstq,push);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)=>Task.WhenAll(
        RunMovementLoopAsync(stoppingToken),ListenCommittedPushesAsync(stoppingToken));

    async Task RunMovementLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                foreach (var playerId in await FindDuePlayersAsync(stoppingToken))
                {
                    await autoBattle.PrepareDueMovementAsync(playerId,stoppingToken);
                    var state = await world.GetAsync(playerId, stoppingToken);
                    if(await farmArrivals.SettleAsync(playerId,stoppingToken)>0)
                        state=await world.GetAsync(playerId,stoppingToken);
                    await push.SendAsync(playerId, "world.updated", state, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { log.LogError(ex, "World movement completion tick failed"); }
        }
    }

    async Task ListenCommittedPushesAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn=await db.DataSource.OpenConnectionAsync(stoppingToken);
                var pending=new ConcurrentQueue<(string channel,string payload)>();
                conn.Notification+=(sender,args)=>
                {
                    if(args.Channel==WorldTreasureBoxState.RewardNotificationChannel||args.Channel==CourtesyService.PendingNotificationChannel)
                        pending.Enqueue((args.Channel,args.Payload));
                };
                await using(var listenWorld=new NpgsqlCommand($"LISTEN {WorldTreasureBoxState.RewardNotificationChannel}",conn))
                    await listenWorld.ExecuteNonQueryAsync(stoppingToken);
                await using(var listenCourtesy=new NpgsqlCommand($"LISTEN {CourtesyService.PendingNotificationChannel}",conn))
                    await listenCourtesy.ExecuteNonQueryAsync(stoppingToken);

                while(!stoppingToken.IsCancellationRequested)
                {
                    await conn.WaitAsync(stoppingToken);
                    while(pending.TryDequeue(out var item))
                    {
                        if(item.channel==WorldTreasureBoxState.RewardNotificationChannel)await ForwardTreasureRewardAsync(item.payload,stoppingToken);
                        else if(item.channel==CourtesyService.PendingNotificationChannel)await ForwardCourtesyAsync(item.payload,stoppingToken);
                    }
                }
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { return; }
            catch(Exception ex)
            {
                log.LogError(ex,"Committed realtime notification listener failed");
                try { await Task.Delay(TimeSpan.FromSeconds(1),stoppingToken); }
                catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { return; }
            }
        }
    }

    async Task ForwardTreasureRewardAsync(string payload,CancellationToken ct)
    {
        using var doc=JsonDocument.Parse(payload);
        var root=doc.RootElement;
        if(!root.TryGetProperty("playerId",out var playerElement)||!root.TryGetProperty("curReward",out var rewardElement))return;
        var playerId=playerElement.GetInt64();var curReward=rewardElement.Clone();
        await push.SendAsync(playerId,"world.treasure",new{curReward},ct);
    }

    async Task ForwardCourtesyAsync(string payload,CancellationToken ct)
    {
        using var doc=JsonDocument.Parse(payload);var root=doc.RootElement;
        if(!root.TryGetProperty("playerId",out var playerElement))return;
        await push.SendAsync(playerElement.GetInt64(),"courtesy.updated",new{liShangWangLai=true},ct);
    }

    async Task<long[]> FindDuePlayersAsync(CancellationToken ct)
    {
        var players = new List<long>();
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
SELECT player_id FROM (
  SELECT DISTINCT player_id
  FROM player_world_moves
  WHERE arrives_at<=now()
  UNION
  SELECT DISTINCT g.player_id
  FROM player_generals g
  JOIN players p ON p.id=g.player_id
  LEFT JOIN player_farms f ON f.player_id=g.player_id AND f.general_id=g.general_id
  WHERE g.general_type=2 AND g.state<=1 AND f.id IS NULL
    AND g.location_id=CASE p.force_id WHEN 1 THEN 254 WHEN 2 THEN 253 WHEN 3 THEN 206 ELSE -1 END
) x
LIMIT 200", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) players.Add(reader.GetInt64(0));
        return players.ToArray();
    }
}

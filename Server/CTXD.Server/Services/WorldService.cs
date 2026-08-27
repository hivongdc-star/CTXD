using CTXD.Server.Data;
using CTXD.Server.Domain;
using CTXD.Server.Models;
using Npgsql;
using System.Text.Json;

namespace CTXD.Server.Services;

public sealed class WorldService(GameDb db, CanonicalContent content, GeneralService generals, NationProgressService nationProgress)
{
    const int Military = 2, Moving = 6, InBattle = 3;
    static readonly IReadOnlyDictionary<int, int> Capitals = new Dictionary<int, int> { [1] = 123, [2] = 19, [3] = 207 };
    static readonly IReadOnlyDictionary<int, int> Farms = new Dictionary<int, int> { [1] = 254, [2] = 253, [3] = 206 };

    public async Task<WorldResponse> GetAsync(long playerId, CancellationToken ct)
    {
        await using var c = await db.DataSource.OpenConnectionAsync(ct);
        await using var t = await c.BeginTransactionAsync(ct);
        var force = await ForceAsync(c, t, playerId, ct); var capital = Capital(force);
        await EnsureCitiesAsync(c, t, ct); await EnsurePlayerAsync(c, t, playerId, force, capital, ct);
        await SettleAsync(c, t, playerId, force, ct);
        var response = await ResponseAsync(c, t, playerId, capital, ct);
        await t.CommitAsync(ct); return response;
    }

    public Task<WorldResponse> MoveAsync(long playerId, int generalId, int cityId, CancellationToken ct) => RouteAsync(playerId, generalId, cityId, false, ct);
    public Task<WorldResponse> AutoMoveAsync(long playerId, int generalId, int cityId, CancellationToken ct) => RouteAsync(playerId, generalId, cityId, true, ct);

    public async Task<IReadOnlyList<WorldScheduledEventNpcView>> GetScheduledEventsAsync(long playerId,CancellationToken ct)
    {await using var c=await db.DataSource.OpenConnectionAsync(ct);var force=await ForceAsync(c,null,playerId,ct);var result=new List<WorldScheduledEventNpcView>();await using var cmd=new NpgsqlCommand("SELECT n.id,n.event_type,n.city_id,n.army_id,n.spawned_at FROM nation_event_npcs n JOIN nation_scheduled_tasks s ON s.id=n.scheduled_task_id WHERE (n.force_id=$1 OR n.force_id=0) AND n.defeated=false AND s.status=0 AND s.starts_at<=now() AND s.ends_at>now() ORDER BY n.city_id,n.id",c);cmd.Parameters.AddWithValue(force);await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))result.Add(new(r.GetInt64(0),r.GetInt16(1),r.GetInt32(2),r.GetInt32(3),r.GetFieldValue<DateTimeOffset>(4)));return result;}

    public async Task<long> StartScheduledEventBattleAsync(long playerId,int generalId,long eventNpcId,CancellationToken ct)
    {await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);var force=await ForceAsync(c,t,playerId,ct);int city,eventType;await using(var npc=new NpgsqlCommand("SELECT n.city_id,n.event_type FROM nation_event_npcs n JOIN nation_scheduled_tasks s ON s.id=n.scheduled_task_id WHERE n.id=$1 AND (n.force_id=$2 OR n.force_id=0) AND n.defeated=false AND s.status=0 AND s.starts_at<=now() AND s.ends_at>now() FOR UPDATE OF n",c,t)){npc.Parameters.AddWithValue(eventNpcId);npc.Parameters.AddWithValue(force);await using var r=await npc.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("WORLD_EVENT_NPC_INACTIVE","Scheduled World event NPC is not active.",409);city=r.GetInt32(0);eventType=r.GetInt16(1);}await using(var general=new NpgsqlCommand("SELECT 1 FROM player_generals WHERE player_id=$1 AND general_id=$2 AND general_type=2 AND state<=1 AND forces>0 AND location_id=$3 FOR UPDATE",c,t)){general.Parameters.AddWithValue(playerId);general.Parameters.AddWithValue(generalId);general.Parameters.AddWithValue(city);if(await general.ExecuteScalarAsync(ct)is null)throw new GameException("WORLD_EVENT_GENERAL_INVALID","General must be ready in the event city.");}long battleId;await using(var add=new NpgsqlCommand("INSERT INTO world_battle_handoffs(city_id,attacker_player_id,attacker_general_id,attacker_force_id,defender_force_id,battle_type,result_payload) VALUES($1,$2,$3,$4,$5,15,jsonb_build_object('eventNpcId',$6,'eventType',$5)) RETURNING id",c,t)){add.Parameters.AddWithValue(city);add.Parameters.AddWithValue(playerId);add.Parameters.AddWithValue(generalId);add.Parameters.AddWithValue(force);add.Parameters.AddWithValue(eventType);add.Parameters.AddWithValue(eventNpcId);battleId=Convert.ToInt64(await add.ExecuteScalarAsync(ct));}await GeneralStateAsync(c,t,playerId,generalId,InBattle,ct);await t.CommitAsync(ct);return battleId;}

    public async Task<WorldBattleResultResponse> ResolveBattleAsync(long battleId, bool attackerWon, string? resultPayload, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(resultPayload))
            try { using var _ = JsonDocument.Parse(resultPayload); }
            catch (JsonException) { throw new GameException("WORLD_BATTLE_RESULT_INVALID", "Battle result payload is not valid JSON."); }
        await using var c = await db.DataSource.OpenConnectionAsync(ct); await using var t = await c.BeginTransactionAsync(ct);
        long player; int general, city, attacker, defender, status, battleType;
        await using (var cmd = new NpgsqlCommand("SELECT attacker_player_id,attacker_general_id,city_id,attacker_force_id,defender_force_id,status,battle_type FROM world_battle_handoffs WHERE id=$1 FOR UPDATE", c, t))
        {
            cmd.Parameters.AddWithValue(battleId); await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) throw new GameException("WORLD_BATTLE_NOT_FOUND", "World battle does not exist.", 404);
            player = r.GetInt64(0); general = r.GetInt32(1); city = r.GetInt32(2); attacker = r.GetInt16(3); defender = r.GetInt16(4); status = r.GetInt16(5); battleType=r.GetInt16(6);
        }
        if (status != 0) throw new GameException("WORLD_BATTLE_RESOLVED", "World battle was already resolved.", 409);
        var winner = attackerWon ? attacker : defender;
        await using (var cmd = new NpgsqlCommand("UPDATE world_battle_handoffs SET status=$2,winner_force_id=$3,result_payload=$4::jsonb,resolved_at=now() WHERE id=$1", c, t))
        { cmd.Parameters.AddWithValue(battleId); cmd.Parameters.AddWithValue(attackerWon ? 1 : 2); cmd.Parameters.AddWithValue(winner); cmd.Parameters.AddWithValue((object?)resultPayload ?? DBNull.Value); await cmd.ExecuteNonQueryAsync(ct); }
        await using (var cmd = new NpgsqlCommand("UPDATE world_cities SET owner_force_id=CASE WHEN $2 AND $4 NOT IN(14,15) THEN $3 ELSE owner_force_id END,state=0,updated_at=now() WHERE city_id=$1", c, t))
        { cmd.Parameters.AddWithValue(city); cmd.Parameters.AddWithValue(attackerWon); cmd.Parameters.AddWithValue(attacker); cmd.Parameters.AddWithValue(battleType); await cmd.ExecuteNonQueryAsync(ct); }
        await GeneralStateAsync(c, t, player, general, 1, ct);
        if (attackerWon) { await GeneralLocationAsync(c, t, player, general, city, ct); if(battleType is not(14 or 15)){await RevealAfterConquestAsync(c, t, attacker, city, ct);await nationProgress.RecordWorldOwnershipAsync(c,t,battleId,player,attacker,city,ct);var assists=new List<long>();await using(var assist=new NpgsqlCommand("SELECT DISTINCT player_id FROM battle_units WHERE battle_id=$1 AND side=1 AND player_id IS NOT NULL AND player_id<>$2",c,t)){assist.Parameters.AddWithValue(battleId);assist.Parameters.AddWithValue(player);await using var r=await assist.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))assists.Add(r.GetInt64(0));}foreach(var id in assists)await nationProgress.AddScoreAsync(c,t,id,attacker,city,2,$"world:battle:{battleId}:score:assist:{id}",ct);} }
        await t.CommitAsync(ct); return new(battleId, city, winner, attackerWon);
    }

    public async Task<WorldCityDetailResponse> GetCityDetailAsync(long playerId, int cityId, CancellationToken ct)
    {
        var world = await GetAsync(playerId, ct);
        var city = world.Cities.FirstOrDefault(x => x.Id == cityId) ?? throw new GameException("WORLD_CITY_NOT_FOUND", "Thanh khong ton tai.", 404);
        if (city.Fogged) throw new GameException("WORLD_CITY_FOGGED", "Thanh nay dang bi suong mu che phu.");
        var battle = world.Battles.FirstOrDefault(x => x.CityId == cityId && x.Status == 0);
        var roster = await generals.GetRosterAsync(playerId, ct);
        return new(city, battle is not null, battle, Neighbors(cityId).OrderBy(x => x).ToArray(), roster.Military.Where(x => x.LocationId == cityId).ToArray());
    }

    async Task<WorldResponse> RouteAsync(long playerId, int generalId, int target, bool auto, CancellationToken ct)
    {
        await using (var c = await db.DataSource.OpenConnectionAsync(ct))
        await using (var t = await c.BeginTransactionAsync(ct))
        {
            var force = await ForceAsync(c, t, playerId, ct); var capital = Capital(force);
            await EnsureCitiesAsync(c, t, ct); await EnsurePlayerAsync(c, t, playerId, force, capital, ct); await SettleAsync(c, t, playerId, force, ct);
            var (location, state, speed) = await GeneralAsync(c, t, playerId, generalId, true, ct);
            if (state is not (0 or 1 or 24)) throw new GameException("WORLD_GENERAL_BUSY", "Vo tuong dang ban, khong the di chuyen.");
            if (location == target) throw new GameException("WORLD_ALREADY_IN_CITY", "Vo tuong dang o thanh nay.");
            if (!content.WorldCities.ContainsKey(target)) throw new GameException("WORLD_CITY_NOT_FOUND", "Thanh khong ton tai.");
            var (seen, attackable) = await VisibilityAsync(c, t, playerId, ct);
            if (!seen.Contains(target) && !attackable.Contains(target)) throw new GameException("WORLD_CITY_FOGGED", "Thanh nay dang bi suong mu che phu.");
            var owners = await OwnersAsync(c, t, ct);
            var path = auto ? Path(location, target, force, seen, attackable, owners) : Direct(location, target);
            if (path.Count < 2) throw new GameException("WORLD_AUTO_NO_ROAD", "Khong tim thay duong hanh quan hop le.");
            if (owners.GetValueOrDefault(target) != force && path.Count == 2) await BattleAsync(c, t, playerId, generalId, force, target, owners.GetValueOrDefault(target), ct);
            else await StartMoveAsync(c, t, playerId, generalId, speed, path, ct);
            await using var focus = new NpgsqlCommand("UPDATE player_world SET focus_general_id=$2,updated_at=now() WHERE player_id=$1", c, t);
            focus.Parameters.AddWithValue(playerId); focus.Parameters.AddWithValue(generalId); await focus.ExecuteNonQueryAsync(ct);
            await t.CommitAsync(ct);
        }
        return await GetAsync(playerId, ct);
    }

    async Task StartMoveAsync(NpgsqlConnection c, NpgsqlTransaction t, long playerId, int generalId, int speed, IReadOnlyList<int> path, CancellationToken ct)
    {
        var road = Road(path[0], path[1])!; var start = DateTimeOffset.UtcNow; var arrival = start.Add(Duration(road.Length, speed));
        await using var cmd = new NpgsqlCommand(@"INSERT INTO player_world_moves(player_id,general_id,road_id,from_city_id,to_city_id,started_at,arrives_at,path_city_ids,path_index)
VALUES($1,$2,$3,$4,$5,$6,$7,$8,1) ON CONFLICT(player_id,general_id) DO NOTHING", c, t);
        cmd.Parameters.AddWithValue(playerId); cmd.Parameters.AddWithValue(generalId); cmd.Parameters.AddWithValue(road.Id); cmd.Parameters.AddWithValue(path[0]);
        cmd.Parameters.AddWithValue(path[1]); cmd.Parameters.AddWithValue(start); cmd.Parameters.AddWithValue(arrival); cmd.Parameters.AddWithValue(path.ToArray());
        if (await cmd.ExecuteNonQueryAsync(ct) == 0) throw new GameException("WORLD_GENERAL_MOVING", "Vo tuong dang di chuyen.");
        await using(var quest=new NpgsqlCommand("UPDATE player_quest_runtime SET world_moves=world_moves+1,updated_at=now() WHERE player_id=$1",c,t)){quest.Parameters.AddWithValue(playerId);await quest.ExecuteNonQueryAsync(ct);}
        await GeneralStateAsync(c, t, playerId, generalId, Moving, ct);
    }

    async Task SettleAsync(NpgsqlConnection c, NpgsqlTransaction t, long playerId, int force, CancellationToken ct)
    {
        while (true)
        {
            int general, city, index; int[] path;
            await using (var cmd = new NpgsqlCommand("SELECT general_id,to_city_id,path_city_ids,path_index FROM player_world_moves WHERE player_id=$1 AND arrives_at<=now() ORDER BY arrives_at LIMIT 1 FOR UPDATE", c, t))
            {
                cmd.Parameters.AddWithValue(playerId); await using var r = await cmd.ExecuteReaderAsync(ct); if (!await r.ReadAsync(ct)) break;
                general = r.GetInt32(0); city = r.GetInt32(1); path = r.GetFieldValue<int[]>(2); index = r.GetInt32(3);
            }
            await GeneralLocationAsync(c, t, playerId, general, city, ct); var next = index + 1;
            if (next >= path.Length) { await DeleteMoveAsync(c, t, playerId, general, ct); await GeneralStateAsync(c, t, playerId, general, 1, ct); continue; }
            var owners = await OwnersAsync(c, t, ct); var nextCity = path[next];
            if (owners.GetValueOrDefault(nextCity) != force) { await DeleteMoveAsync(c, t, playerId, general, ct); await BattleAsync(c, t, playerId, general, force, nextCity, owners.GetValueOrDefault(nextCity), ct); continue; }
            var (_, _, speed) = await GeneralAsync(c, t, playerId, general, false, ct); var road = Road(city, nextCity)!;
            var start = DateTimeOffset.UtcNow; var arrival = start.Add(Duration(road.Length, speed));
            await using var update = new NpgsqlCommand("UPDATE player_world_moves SET road_id=$3,from_city_id=$4,to_city_id=$5,started_at=$6,arrives_at=$7,path_index=$8 WHERE player_id=$1 AND general_id=$2", c, t);
            update.Parameters.AddWithValue(playerId); update.Parameters.AddWithValue(general); update.Parameters.AddWithValue(road.Id); update.Parameters.AddWithValue(city);
            update.Parameters.AddWithValue(nextCity); update.Parameters.AddWithValue(start); update.Parameters.AddWithValue(arrival); update.Parameters.AddWithValue(next); await update.ExecuteNonQueryAsync(ct);
        }
    }

    List<int> Path(int start, int target, int force, HashSet<int> seen, HashSet<int> attackable, IReadOnlyDictionary<int, int> owners)
    {
        var allowed = seen.Where(x => owners.GetValueOrDefault(x) == force).ToHashSet(); allowed.Add(start);
        if (seen.Contains(target) || attackable.Contains(target)) allowed.Add(target);
        var dist = new Dictionary<int, long> { [start] = 0 }; var prev = new Dictionary<int, int>(); var q = new PriorityQueue<int, long>(); q.Enqueue(start, 0);
        while (q.TryDequeue(out var city, out var cost)) { if (city == target) break; if (dist.GetValueOrDefault(city, long.MaxValue) != cost) continue;
            foreach (var next in Neighbors(city).Where(allowed.Contains)) { var nc = cost + Road(city, next)!.Length; if (nc >= dist.GetValueOrDefault(next, long.MaxValue)) continue; dist[next] = nc; prev[next] = city; q.Enqueue(next, nc); } }
        if (!dist.ContainsKey(target)) return []; var result = new List<int>(); for (var at = target; ; at = prev[at]) { result.Add(at); if (at == start) break; } result.Reverse(); return result;
    }
    List<int> Direct(int a, int b) => Road(a, b) is null ? [] : [a, b];
    IEnumerable<int> Neighbors(int city) => content.WorldRoads.Values.SelectMany(x => x.Start == city ? new[] { x.End } : x.End == city ? new[] { x.Start } : []);
    WorldRoadDefinition? Road(int a, int b) => content.WorldRoads.Values.FirstOrDefault(x => x.Start == a && x.End == b || x.Start == b && x.End == a);

    async Task<WorldResponse> ResponseAsync(NpgsqlConnection c, NpgsqlTransaction t, long playerId, int capital, CancellationToken ct)
    {
        var (seen, attackable) = await VisibilityAsync(c, t, playerId, ct); int? focus;
        await using (var cmd = new NpgsqlCommand("SELECT focus_general_id FROM player_world WHERE player_id=$1", c, t)) { cmd.Parameters.AddWithValue(playerId); var v = await cmd.ExecuteScalarAsync(ct); focus = v is null or DBNull ? null : Convert.ToInt32(v); }
        var runtime = new Dictionary<int, (int owner, int state, int title, int border)>();
        await using (var cmd = new NpgsqlCommand("SELECT city_id,owner_force_id,state,title,border FROM world_cities", c, t)) await using (var r = await cmd.ExecuteReaderAsync(ct)) while (await r.ReadAsync(ct)) runtime[r.GetInt32(0)] = (r.GetInt16(1), r.GetInt16(2), r.GetInt16(3), r.GetInt16(4));
        var moves = new List<WorldMoveView>();
        await using (var cmd = new NpgsqlCommand("SELECT general_id,road_id,from_city_id,to_city_id,started_at,arrives_at,path_city_ids,path_index FROM player_world_moves WHERE player_id=$1 ORDER BY general_id", c, t)) { cmd.Parameters.AddWithValue(playerId); await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) moves.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetFieldValue<DateTimeOffset>(4), r.GetFieldValue<DateTimeOffset>(5), r.GetFieldValue<int[]>(6), r.GetInt32(7))); }
        var battles = new List<WorldBattleHandoffView>();
        await using (var cmd = new NpgsqlCommand("SELECT id,city_id,attacker_player_id,attacker_general_id,attacker_force_id,defender_force_id,battle_type,status,winner_force_id,created_at,resolved_at FROM world_battle_handoffs WHERE status=0", c, t)) await using (var r = await cmd.ExecuteReaderAsync(ct)) while (await r.ReadAsync(ct)) battles.Add(new(r.GetInt64(0), r.GetInt32(1), r.GetInt64(2), r.GetInt32(3), r.GetInt16(4), r.GetInt16(5), r.GetInt16(6), r.GetInt16(7), r.IsDBNull(8) ? null : r.GetInt16(8), r.GetFieldValue<DateTimeOffset>(9), r.IsDBNull(10) ? null : r.GetFieldValue<DateTimeOffset>(10)));
        var cities = content.WorldCities.Values.OrderBy(x => x.Id).Select(x => { var s = runtime.GetValueOrDefault(x.Id); return View(x, s.owner, s.state, s.title, s.border, seen.Contains(x.Id), attackable.Contains(x.Id)); }).ToArray();
        return new(capital, focus, cities, content.WorldRoads.Values.OrderBy(x => x.Id).Select(x => new WorldRoadView(x.Id, x.Start, x.End, x.Length, x.Trace)).ToArray(), moves, battles);
    }

    async Task EnsureCitiesAsync(NpgsqlConnection c, NpgsqlTransaction t, CancellationToken ct)
    { foreach (var city in content.WorldCities.Values) { var owner = InitialOwner(city); await using var cmd = new NpgsqlCommand("INSERT INTO world_cities(city_id,owner_force_id) VALUES($1,$2) ON CONFLICT(city_id) DO NOTHING", c, t); cmd.Parameters.AddWithValue(city.Id); cmd.Parameters.AddWithValue(owner); await cmd.ExecuteNonQueryAsync(ct); } }
    async Task EnsurePlayerAsync(NpgsqlConnection c, NpgsqlTransaction t, long player, int force, int capital, CancellationToken ct)
    { var area = Area(content.WorldCities[capital], force); var seen = content.WorldCities.Values.Where(x => Area(x, force) == area).Select(x => x.Id).ToHashSet(); if (seen.Count == 0) seen.Add(capital);
      var areas = content.WorldRoads.Values.SelectMany(x => seen.Contains(x.Start) && !seen.Contains(x.End) ? new[] { x.End } : seen.Contains(x.End) && !seen.Contains(x.Start) ? new[] { x.Start } : []).Where(content.WorldCities.ContainsKey).Select(x => Area(content.WorldCities[x], force)).ToHashSet();
      var attackable = content.WorldCities.Values.Where(x => areas.Contains(Area(x, force))).Select(x => x.Id).Where(x => !seen.Contains(x)).Distinct().OrderBy(x => x).ToArray();
      await using var cmd = new NpgsqlCommand("INSERT INTO player_world(player_id,discovered_city_ids,attackable_city_ids) VALUES($1,$2,$3) ON CONFLICT(player_id) DO NOTHING", c, t); cmd.Parameters.AddWithValue(player); cmd.Parameters.AddWithValue(seen.OrderBy(x => x).ToArray()); cmd.Parameters.AddWithValue(attackable); await cmd.ExecuteNonQueryAsync(ct); }
    async Task BattleAsync(NpgsqlConnection c, NpgsqlTransaction t, long player, int general, int force, int city, int defender, CancellationToken ct)
    { await using var cmd = new NpgsqlCommand("INSERT INTO world_battle_handoffs(city_id,attacker_player_id,attacker_general_id,attacker_force_id,defender_force_id,battle_type) VALUES($1,$2,$3,$4,$5,$6) ON CONFLICT DO NOTHING", c, t); cmd.Parameters.AddWithValue(city); cmd.Parameters.AddWithValue(player); cmd.Parameters.AddWithValue(general); cmd.Parameters.AddWithValue(force); cmd.Parameters.AddWithValue(defender); cmd.Parameters.AddWithValue(city is 250 or 251 or 252 ? 14 : 3); if (await cmd.ExecuteNonQueryAsync(ct) == 0) throw new GameException("WORLD_BATTLE_ACTIVE", "Thanh nay da co tran chien."); await GeneralStateAsync(c, t, player, general, InBattle, ct); await using var state = new NpgsqlCommand("UPDATE world_cities SET state=1,updated_at=now() WHERE city_id=$1", c, t); state.Parameters.AddWithValue(city); await state.ExecuteNonQueryAsync(ct); }

    async Task RevealAfterConquestAsync(NpgsqlConnection c, NpgsqlTransaction t, int force, int city, CancellationToken ct)
    {
        var adjacent = Neighbors(city).Distinct().ToArray(); var players = new List<long>();
        await using (var cmd = new NpgsqlCommand("SELECT pw.player_id FROM player_world pw JOIN players p ON p.id=pw.player_id WHERE p.force_id=$1 FOR UPDATE OF pw", c, t))
        { cmd.Parameters.AddWithValue(force); await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) players.Add(r.GetInt64(0)); }
        foreach (var player in players)
        {
            var (seen, attackable) = await VisibilityAsync(c, t, player, ct); seen.Add(city); attackable.Remove(city);
            foreach (var next in adjacent) if (!seen.Contains(next)) attackable.Add(next);
            await using var cmd = new NpgsqlCommand("UPDATE player_world SET discovered_city_ids=$2,attackable_city_ids=$3,updated_at=now() WHERE player_id=$1", c, t);
            cmd.Parameters.AddWithValue(player); cmd.Parameters.AddWithValue(seen.OrderBy(x => x).ToArray()); cmd.Parameters.AddWithValue(attackable.OrderBy(x => x).ToArray()); await cmd.ExecuteNonQueryAsync(ct);
        }
    }
    async Task<(int location, int state, int speed)> GeneralAsync(NpgsqlConnection c, NpgsqlTransaction t, long player, int id, bool locked, CancellationToken ct)
    { await using var cmd = new NpgsqlCommand($"SELECT location_id,state FROM player_generals WHERE player_id=$1 AND general_id=$2 AND general_type=$3{(locked ? " FOR UPDATE" : "")}", c, t); cmd.Parameters.AddWithValue(player); cmd.Parameters.AddWithValue(id); cmd.Parameters.AddWithValue(Military); await using var r = await cmd.ExecuteReaderAsync(ct); if (!await r.ReadAsync(ct)) throw new GameException("WORLD_GENERAL_NOT_FOUND", "Vo tuong khong ton tai hoac khong phai vo tuong quan su."); if (!content.Generals.TryGetValue(id, out var g) || !content.Troops.TryGetValue(g.TroopId, out var troop)) throw new GameException("WORLD_MOVE_SPEED_MISSING", "Khong tim thay toc do binh chung.", 500); return (r.GetInt32(0), r.GetInt16(1), troop.Speed); }
    static async Task<(HashSet<int> seen, HashSet<int> attackable)> VisibilityAsync(NpgsqlConnection c, NpgsqlTransaction t, long player, CancellationToken ct) { await using var cmd = new NpgsqlCommand("SELECT discovered_city_ids,attackable_city_ids FROM player_world WHERE player_id=$1", c, t); cmd.Parameters.AddWithValue(player); await using var r = await cmd.ExecuteReaderAsync(ct); await r.ReadAsync(ct); return (r.GetFieldValue<int[]>(0).ToHashSet(), r.GetFieldValue<int[]>(1).ToHashSet()); }
    static async Task<Dictionary<int, int>> OwnersAsync(NpgsqlConnection c, NpgsqlTransaction t, CancellationToken ct) { var d = new Dictionary<int, int>(); await using var cmd = new NpgsqlCommand("SELECT city_id,owner_force_id FROM world_cities", c, t); await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) d[r.GetInt32(0)] = r.GetInt16(1); return d; }
    static async Task<int> ForceAsync(NpgsqlConnection c, NpgsqlTransaction? t, long player, CancellationToken ct) { await using var cmd = new NpgsqlCommand("SELECT force_id FROM players WHERE id=$1", c, t); cmd.Parameters.AddWithValue(player); var v = await cmd.ExecuteScalarAsync(ct); return v is null ? throw new GameException("PLAYER_NOT_FOUND", "Khong tim thay nhan vat.", 404) : Convert.ToInt32(v); }
    static async Task GeneralStateAsync(NpgsqlConnection c, NpgsqlTransaction t, long p, int g, int s, CancellationToken ct) { await using var cmd = new NpgsqlCommand("UPDATE player_generals SET state=$3,updated_at=now() WHERE player_id=$1 AND general_id=$2", c, t); cmd.Parameters.AddWithValue(p); cmd.Parameters.AddWithValue(g); cmd.Parameters.AddWithValue(s); await cmd.ExecuteNonQueryAsync(ct); }
    static async Task GeneralLocationAsync(NpgsqlConnection c, NpgsqlTransaction t, long p, int g, int city, CancellationToken ct) { await using var cmd = new NpgsqlCommand("UPDATE player_generals SET location_id=$3,updated_at=now() WHERE player_id=$1 AND general_id=$2", c, t); cmd.Parameters.AddWithValue(p); cmd.Parameters.AddWithValue(g); cmd.Parameters.AddWithValue(city); await cmd.ExecuteNonQueryAsync(ct); }
    static async Task DeleteMoveAsync(NpgsqlConnection c, NpgsqlTransaction t, long p, int g, CancellationToken ct) { await using var cmd = new NpgsqlCommand("DELETE FROM player_world_moves WHERE player_id=$1 AND general_id=$2", c, t); cmd.Parameters.AddWithValue(p); cmd.Parameters.AddWithValue(g); await cmd.ExecuteNonQueryAsync(ct); }
    static TimeSpan Duration(int length, int speed) { if (speed <= 0) throw new GameException("WORLD_MOVE_SPEED_INVALID", "Toc do binh chung khong hop le.", 500); return TimeSpan.FromMilliseconds(Math.Max(0, (long)((double)length / speed * 60_000d) / (DateTime.Now.Hour < 8 ? 3 : 2) / 4L)); }
    static int Area(WorldCityDefinition x, int f) => f switch { 1 => x.WeiArea, 2 => x.ShuArea, 3 => x.WuArea, _ => 0 };
    int InitialOwner(WorldCityDefinition city)
    {
        var fixedOwner = Capitals.FirstOrDefault(x => x.Value == city.Id).Key;
        if (fixedOwner == 0) fixedOwner = Farms.FirstOrDefault(x => x.Value == city.Id).Key;
        if (fixedOwner != 0) return fixedOwner;
        foreach (var force in Capitals.Keys)
        {
            var capitalArea = Area(content.WorldCities[Capitals[force]], force);
            if (capitalArea != 0 && Area(city, force) == capitalArea) return force;
        }
        return 0;
    }
    static int Capital(int force) => Capitals.TryGetValue(force, out var city) ? city : throw new GameException("WORLD_FORCE_REQUIRED", "Can chon quoc gia truoc khi vao World.");
    static WorldCityView View(WorldCityDefinition x, int owner, int state, int title, int border, bool seen, bool attackable) => new(x.Id, x.Name, x.Type, x.Terrain, x.TerrainEffectType, x.Output, x.Chief, x.Npcs, x.WeiDistance, x.ShuDistance, x.WuDistance, x.WeiArea, x.ShuArea, x.WuArea, x.WeiMask, x.ShuMask, x.WuMask, x.ShowMask, x.Pic, x.Intro, x.X, x.Y, x.Model, owner, state, title, border, seen, attackable, !seen && !attackable);
}

using CTXD.Server.Data;
using CTXD.Server.Domain;
using CTXD.Server.Models;
using Npgsql;

namespace CTXD.Server.Services;

public sealed class TavernService(
    GameDb db,
    CanonicalContent content,
    GeneralService generals,
    TutorialService tutorial,
    ResourceProductionService resources,
    DstqActivityService dstq)
{
    const int CivilType = 1;
    const int MilitaryType = 2;
    const int CivilOpenFunction = 44;
    const int MilitaryOpenFunction = 45;
    const int RefreshFunction = 55;
    static readonly TimeSpan RefreshCooldown = TimeSpan.FromMinutes(20); // legacy refreshGeneral adds 1,200,000 ms.
    static readonly IReadOnlyDictionary<int, int> NationCapital = new Dictionary<int, int> { [1] = 123, [2] = 19, [3] = 207 };

    public async Task<TavernResponse> GetAsync(long playerId, int type, CancellationToken ct)
    {
        ValidateType(type);
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var player = await ReadPlayerAsync(conn, tx, playerId, ct);
        await RequireTypeOpenAsync(conn, tx, playerId, type, ct);
        var tavern = await EnsureTavernAsync(conn, tx, playerId, ct);

        var offers = await ReadOffersAsync(conn, tx, playerId, type, ct);
        if (offers.Count == 0)
        {
            // Legacy getGeneral() performs an internal forced/free refresh when function 55 is not open yet.
            await GenerateOffersAsync(conn, tx, player, tavern, type, initialFreeRefresh: true, ct);
            offers = await ReadOffersAsync(conn, tx, playerId, type, ct);
        }

        var result = await BuildResponseAsync(conn, tx, player, tavern, type, offers, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<TavernResponse> RefreshAsync(long playerId, int type, CancellationToken ct)
    {
        ValidateType(type);
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var player = await ReadPlayerAsync(conn, tx, playerId, ct);
        await RequireTypeOpenAsync(conn, tx, playerId, type, ct);
        await RequireFunctionAsync(conn, tx, playerId, RefreshFunction, "TAVERN_REFRESH_LOCKED", "Chưa mở chức năng thăm dò Tửu Quán.", ct);
        var tavern = await EnsureTavernAsync(conn, tx, playerId, ct, forUpdate: true);

        var next = type == CivilType ? tavern.NextCivilAt : tavern.NextMilitaryAt;
        var now = DateTimeOffset.UtcNow;
        if (next > now)
            throw new GameException("TAVERN_REFRESH_CD", $"Tửu Quán đang hồi, còn {Math.Ceiling((next-now).TotalSeconds)} giây.");

        // Current legacy configuration consumes 0 copper for normal refresh at this point in refreshGeneral.
        // Keep the transaction path explicit so a later imported rule can replace the zero without client changes.
        await resources.AccrueAndGetAsync(playerId, ct, conn, tx);

        var nextState = DecideNextState(tavern.State);
        var nextAt = now.Add(RefreshCooldown);
        if (type == CivilType)
        {
            tavern = tavern with { State = nextState, CivilRefreshTime = tavern.CivilRefreshTime + 1, NextCivilAt = nextAt };
            await using var u = new NpgsqlCommand(@"UPDATE player_tavern SET tavern_state=$2,civil_refresh_time=civil_refresh_time+1,next_civil_at=$3,updated_at=now() WHERE player_id=$1", conn, tx);
            u.Parameters.AddWithValue(playerId); u.Parameters.AddWithValue(nextState); u.Parameters.AddWithValue(nextAt); await u.ExecuteNonQueryAsync(ct);
        }
        else
        {
            tavern = tavern with { State = nextState, MilitaryRefreshTime = tavern.MilitaryRefreshTime + 1, NextMilitaryAt = nextAt };
            await using var u = new NpgsqlCommand(@"UPDATE player_tavern SET tavern_state=$2,military_refresh_time=military_refresh_time+1,next_military_at=$3,updated_at=now() WHERE player_id=$1", conn, tx);
            u.Parameters.AddWithValue(playerId); u.Parameters.AddWithValue(nextState); u.Parameters.AddWithValue(nextAt); await u.ExecuteNonQueryAsync(ct);
        }

        await GenerateOffersAsync(conn, tx, player, tavern, type, initialFreeRefresh: false, ct);
        await tutorial.TryCompleteAsync(conn, tx, playerId, "tavern_refresh", [], ct);
        var offers = await ReadOffersAsync(conn, tx, playerId, type, ct);
        var result = await BuildResponseAsync(conn, tx, player, tavern, type, offers, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<TavernResponse> SetLockedAsync(long playerId, int generalId, bool locked, CancellationToken ct)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var player = await ReadPlayerAsync(conn, tx, playerId, ct);
        var tavern = await EnsureTavernAsync(conn, tx, playerId, ct, forUpdate: true);

        int type;
        await using (var r = new NpgsqlCommand("SELECT general_type,bought,locked FROM player_tavern_offers WHERE player_id=$1 AND general_id=$2 FOR UPDATE", conn, tx))
        {
            r.Parameters.AddWithValue(playerId); r.Parameters.AddWithValue(generalId);
            await using var rd = await r.ExecuteReaderAsync(ct);
            if (!await rd.ReadAsync(ct)) throw new GameException("TAVERN_OFFER_MISSING", "Võ tướng không có trong Tửu Quán.");
            type = rd.GetInt16(0);
            if (rd.GetBoolean(1)) throw new GameException("TAVERN_ALREADY_RECRUITED", "Võ tướng đã được chiêu mộ.");
        }
        await RequireTypeOpenAsync(conn, tx, playerId, type, ct);
        await using (var u = new NpgsqlCommand("UPDATE player_tavern_offers SET locked=$3 WHERE player_id=$1 AND general_id=$2", conn, tx))
        { u.Parameters.AddWithValue(playerId); u.Parameters.AddWithValue(generalId); u.Parameters.AddWithValue(locked); await u.ExecuteNonQueryAsync(ct); }

        var offers = await ReadOffersAsync(conn, tx, playerId, type, ct);
        var result = await BuildResponseAsync(conn, tx, player, tavern, type, offers, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<RecruitGeneralResponse> RecruitAsync(long playerId, int generalId, CancellationToken ct)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var player = await ReadPlayerAsync(conn, tx, playerId, ct, forUpdate: true);
        if (!content.Generals.TryGetValue(generalId, out var definition) || definition.Type is not (CivilType or MilitaryType))
            throw new GameException("GENERAL_NOT_FOUND", "Không có võ tướng này.");
        await RequireTypeOpenAsync(conn, tx, playerId, definition.Type, ct);

        TavernOfferRow offer;
        await using (var cmd = new NpgsqlCommand(@"SELECT general_type,position,locked,bought,is_gold,price FROM player_tavern_offers WHERE player_id=$1 AND general_id=$2 FOR UPDATE", conn, tx))
        {
            cmd.Parameters.AddWithValue(playerId); cmd.Parameters.AddWithValue(generalId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) throw new GameException("GENERAL_NOT_REFRESHED", "Võ tướng chưa xuất hiện trong Tửu Quán.");
            offer = new TavernOfferRow(generalId, r.GetInt16(0), r.GetInt32(1), r.GetBoolean(2), r.GetBoolean(3), r.GetBoolean(4), r.GetInt32(5));
        }
        if (offer.Bought) throw new GameException("GENERAL_ALREADY_RECRUITED", "Võ tướng đã được chiêu mộ.");

        int owned;
        await using (var c = new NpgsqlCommand("SELECT count(*) FROM player_generals WHERE player_id=$1 AND general_type=$2", conn, tx))
        { c.Parameters.AddWithValue(playerId); c.Parameters.AddWithValue(definition.Type); owned = Convert.ToInt32(await c.ExecuteScalarAsync(ct)); }
        var max = await generals.MaxPositionCountAsync(playerId, player.Level, definition.Type, ct, conn, tx);
        if (owned >= max) throw new GameException("GENERAL_SLOT_FULL", "Số võ tướng đã đạt giới hạn.");
        await using (var exists = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM player_generals WHERE player_id=$1 AND general_id=$2)", conn, tx))
        { exists.Parameters.AddWithValue(playerId); exists.Parameters.AddWithValue(generalId); if ((bool)(await exists.ExecuteScalarAsync(ct))!) throw new GameException("GENERAL_ALREADY_RECRUITED", "Võ tướng đã được chiêu mộ."); }

        if (offer.IsGold)
        {
            var totalGold = player.SysGold + player.UserGold;
            if (totalGold < offer.Price) throw new GameException("GOLD_NOT_ENOUGH", "Hoàng kim không đủ.");
            var useUser = Math.Min(player.UserGold, offer.Price);
            var useSys = offer.Price - useUser;
            await using var pay = new NpgsqlCommand("UPDATE players SET user_gold=user_gold-$2,sys_gold=sys_gold-$3,updated_at=now() WHERE id=$1", conn, tx);
            pay.Parameters.AddWithValue(playerId); pay.Parameters.AddWithValue(useUser); pay.Parameters.AddWithValue(useSys); await pay.ExecuteNonQueryAsync(ct);
            await dstq.RecordGoldSpendAsync(conn,tx,playerId,offer.Price,ct);
        }
        else
        {
            await resources.AccrueAndGetAsync(playerId, ct, conn, tx);
            await using var pay = new NpgsqlCommand("UPDATE player_resources SET copper=copper-$2 WHERE player_id=$1 AND copper >= $2", conn, tx);
            pay.Parameters.AddWithValue(playerId); pay.Parameters.AddWithValue(offer.Price);
            if (await pay.ExecuteNonQueryAsync(ct) == 0) throw new GameException("COPPER_NOT_ENOUGH", "Bạc không đủ.");
        }

        var capital = definition.Type == MilitaryType && NationCapital.TryGetValue(player.ForceId, out var capitalCityId) ? capitalCityId : 0;
        // Legacy creates a new recruit at level 1/exp 0, with zero bonus attributes; military morale=100 and auto=1.
        await using (var add = new NpgsqlCommand(@"INSERT INTO player_generals(player_id,general_id,general_type,level,exp,forces,location_id,state,morale,auto_state)
VALUES($1,$2,$3,1,0,0,$4,1,100,1)", conn, tx))
        { add.Parameters.AddWithValue(playerId); add.Parameters.AddWithValue(generalId); add.Parameters.AddWithValue(definition.Type); add.Parameters.AddWithValue(capital); await add.ExecuteNonQueryAsync(ct); }
        await using (var bought = new NpgsqlCommand("UPDATE player_tavern_offers SET bought=TRUE,locked=FALSE WHERE player_id=$1 AND general_id=$2", conn, tx))
        { bought.Parameters.AddWithValue(playerId); bought.Parameters.AddWithValue(generalId); await bought.ExecuteNonQueryAsync(ct); }

        await tutorial.TryCompleteAsync(conn, tx, playerId, "recruit_general", [definition.Type, generalId], ct);
        var view = ToGeneralView(definition, definition.Type, 1, 0, 0, 0, 0, 0, 0, capital, 1, 100, 1);
        var resource = await resources.AccrueAndGetAsync(playerId, ct, conn, tx);
        await tx.CommitAsync(ct);
        return new RecruitGeneralResponse(view, resource, owned + 1, max);
    }

    async Task GenerateOffersAsync(NpgsqlConnection conn, NpgsqlTransaction tx, PlayerRow player, TavernRow tavern, int type, bool initialFreeRefresh, CancellationToken ct)
    {
        var old = await ReadOffersAsync(conn, tx, player.Id, type, ct);
        var locked = old.Where(x => x.Locked && !x.Bought).OrderBy(x => x.Position).ToList();
        var tutorialLocks = ParseIdSet(tavern.LockedGeneralIds);
        var keepIds = locked.Select(x => x.GeneralId).ToHashSet();

        // The task reward tavern_lock_on forces its target general to remain available until recruited.
        foreach (var id in tutorialLocks)
        {
            if (!content.Generals.TryGetValue(id, out var g) || g.Type != type || keepIds.Contains(id)) continue;
            var position = FirstFreePosition(locked.Select(x => x.Position));
            var price = DecidePrice(id, forceCopperMax: initialFreeRefresh);
            locked.Add(new TavernOfferView(position, id, g.Name, g.Pic, g.Quality, g.Type, false, false, price.IsGold, price.Price,
                g.Leader, g.Strength, g.Intel, g.Politics, g.TroopId, g.TacticId, g.StratagemId));
            keepIds.Add(id);
        }

        await using (var del = new NpgsqlCommand("DELETE FROM player_tavern_offers WHERE player_id=$1 AND general_type=$2", conn, tx))
        { del.Parameters.AddWithValue(player.Id); del.Parameters.AddWithValue(type); await del.ExecuteNonQueryAsync(ct); }

        var usedPositions = new HashSet<int>();
        foreach (var x in locked.Take(5))
        {
            var pos = x.Position is >= 1 and <= 5 && usedPositions.Add(x.Position) ? x.Position : FirstFreePosition(usedPositions);
            usedPositions.Add(pos);
            await InsertOfferAsync(conn, tx, player.Id, type, pos, x.GeneralId, true, false, x.IsGold, x.Price, ct);
        }

        var ownedIds = new HashSet<int>();
        await using (var cmd = new NpgsqlCommand("SELECT general_id FROM player_generals WHERE player_id=$1", conn, tx))
        { cmd.Parameters.AddWithValue(player.Id); await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) ownedIds.Add(r.GetInt32(0)); }

        var refreshTime = type == CivilType ? tavern.CivilRefreshTime : tavern.MilitaryRefreshTime;
        var candidates = content.GeneralRecruits.Values
            .Where(r => r.Type == type && r.MinRefreshTime <= refreshTime && !ownedIds.Contains(r.GeneralId) && !keepIds.Contains(r.GeneralId))
            .Select(r => content.Generals.GetValueOrDefault(r.GeneralId))
            .Where(g => g is not null)
            .Cast<GeneralDefinition>()
            .ToList();

        // Tavern state maps to general quality in the legacy GeneralCache selection path. If the exact-quality
        // bucket cannot fill the five slots, legacy falls back to a broader filtered list; mirror that behavior.
        var primary = candidates.Where(g => g.Quality == tavern.State).OrderBy(_ => Random.Shared.Next()).ToList();
        var fallback = candidates.Where(g => g.Quality != tavern.State).OrderByDescending(g => g.Quality == 1).ThenBy(_ => Random.Shared.Next()).ToList();
        var sequence = primary.Concat(fallback).DistinctBy(g => g.Id);
        foreach (var g in sequence)
        {
            if (usedPositions.Count >= 5) break;
            var pos = FirstFreePosition(usedPositions); usedPositions.Add(pos);
            var price = DecidePrice(g.Id, forceCopperMax: initialFreeRefresh);
            await InsertOfferAsync(conn, tx, player.Id, type, pos, g.Id, false, false, price.IsGold, price.Price, ct);
        }
    }

    async Task<TavernResponse> BuildResponseAsync(NpgsqlConnection conn, NpgsqlTransaction tx, PlayerRow player, TavernRow tavern, int type, IReadOnlyList<TavernOfferView> offers, CancellationToken ct)
    {
        int owned;
        await using (var c = new NpgsqlCommand("SELECT count(*) FROM player_generals WHERE player_id=$1 AND general_type=$2", conn, tx))
        { c.Parameters.AddWithValue(player.Id); c.Parameters.AddWithValue(type); owned = Convert.ToInt32(await c.ExecuteScalarAsync(ct)); }
        var max = await generals.MaxPositionCountAsync(player.Id, player.Level, type, ct, conn, tx);
        var next = type == CivilType ? tavern.NextCivilAt : tavern.NextMilitaryAt;
        var count = type == CivilType ? tavern.CivilRefreshTime : tavern.MilitaryRefreshTime;
        return new TavernResponse(type, tavern.State, count, next, owned, max, offers);
    }

    async Task<IReadOnlyList<TavernOfferView>> ReadOffersAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, int type, CancellationToken ct)
    {
        var list = new List<TavernOfferView>();
        await using var cmd = new NpgsqlCommand(@"SELECT o.position,o.general_id,o.locked,o.bought,o.is_gold,o.price
FROM player_tavern_offers o WHERE o.player_id=$1 AND o.general_type=$2 ORDER BY o.position", conn, tx);
        cmd.Parameters.AddWithValue(playerId); cmd.Parameters.AddWithValue(type);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var id = r.GetInt32(1);
            if (!content.Generals.TryGetValue(id, out var g)) continue;
            list.Add(new TavernOfferView(r.GetInt32(0), id, g.Name, g.Pic, g.Quality, g.Type,
                r.GetBoolean(2), r.GetBoolean(3), r.GetBoolean(4), r.GetInt32(5),
                g.Leader, g.Strength, g.Intel, g.Politics, g.TroopId, g.TacticId, g.StratagemId));
        }
        return list;
    }

    (bool IsGold, int Price) DecidePrice(int generalId, bool forceCopperMax)
    {
        if (!content.GeneralRecruitByGeneralId.TryGetValue(generalId, out var r)) return (false, 0);
        var isGold = !forceCopperMax && Random.Shared.NextDouble() >= 1d - r.GoldProb;
        var basePrice = isGold ? r.GoldMax : r.CopperMax;

        var multipliers = ParseDoubleList(content.StringConstants.GetValueOrDefault(3)?.Value, [1d]);
        var probs = ParseDoubleList(content.StringConstants.GetValueOrDefault(4)?.Value, [1d]);
        var roll = Random.Shared.NextDouble();
        var cumulative = 0d; var index = 0;
        for (var i = 0; i < probs.Length; i++) { cumulative += probs[i]; if (roll <= cumulative) { index = i; break; } }
        var multiplier = index < multipliers.Length ? multipliers[index] : 1d;
        return (isGold, (int)Math.Round(basePrice * multiplier, MidpointRounding.AwayFromZero));
    }

    int DecideNextState(int preState)
    {
        var rows = content.TavernTransitionsFrom(preState);
        if (rows.Count == 0) return preState;
        var roll = Random.Shared.NextDouble();
        var cumulative = 0d;
        foreach (var row in rows)
        {
            cumulative += row.Probability;
            if (roll <= cumulative) return row.NextState;
        }
        return rows[^1].NextState; // legacy forces the last cumulative bucket to 1.0.
    }

    async Task RequireTypeOpenAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, int type, CancellationToken ct) =>
        await RequireFunctionAsync(conn, tx, playerId, type == CivilType ? CivilOpenFunction : MilitaryOpenFunction,
            "TAVERN_LOCKED", "Tửu Quán chưa mở.", ct);

    static async Task RequireFunctionAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, int functionId, string code, string message, CancellationToken ct)
    {
        await using var c = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM player_functions WHERE player_id=$1 AND function_id=$2)", conn, tx);
        c.Parameters.AddWithValue(playerId); c.Parameters.AddWithValue(functionId);
        if (!(bool)(await c.ExecuteScalarAsync(ct))!) throw new GameException(code, message);
    }

    async Task<TavernRow> EnsureTavernAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, CancellationToken ct, bool forUpdate = false)
    {
        await using (var ins = new NpgsqlCommand("INSERT INTO player_tavern(player_id) VALUES($1) ON CONFLICT DO NOTHING", conn, tx))
        { ins.Parameters.AddWithValue(playerId); await ins.ExecuteNonQueryAsync(ct); }
        var sql = @"SELECT tavern_state,civil_refresh_time,military_refresh_time,next_civil_at,next_military_at,locked_general_ids
FROM player_tavern WHERE player_id=$1" + (forUpdate ? " FOR UPDATE" : "");
        await using var cmd = new NpgsqlCommand(sql, conn, tx); cmd.Parameters.AddWithValue(playerId);
        await using var r = await cmd.ExecuteReaderAsync(ct); await r.ReadAsync(ct);
        return new TavernRow(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetFieldValue<DateTimeOffset>(3), r.GetFieldValue<DateTimeOffset>(4), r.GetString(5));
    }

    static async Task<PlayerRow> ReadPlayerAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, CancellationToken ct, bool forUpdate = false)
    {
        var sql = "SELECT id,level,force_id,sys_gold,user_gold,consume_level FROM players WHERE id=$1" + (forUpdate ? " FOR UPDATE" : "");
        await using var cmd = new NpgsqlCommand(sql, conn, tx); cmd.Parameters.AddWithValue(playerId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy nhân vật.", 404);
        return new PlayerRow(r.GetInt64(0), r.GetInt32(1), r.GetInt16(2), r.GetInt64(3), r.GetInt64(4), r.GetInt32(5));
    }

    static async Task InsertOfferAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, int type, int pos, int generalId, bool locked, bool bought, bool isGold, int price, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(@"INSERT INTO player_tavern_offers(player_id,general_type,position,general_id,locked,bought,is_gold,price)
VALUES($1,$2,$3,$4,$5,$6,$7,$8)", conn, tx);
        cmd.Parameters.AddWithValue(playerId); cmd.Parameters.AddWithValue(type); cmd.Parameters.AddWithValue(pos); cmd.Parameters.AddWithValue(generalId);
        cmd.Parameters.AddWithValue(locked); cmd.Parameters.AddWithValue(bought); cmd.Parameters.AddWithValue(isGold); cmd.Parameters.AddWithValue(price);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    GeneralView ToGeneralView(GeneralDefinition g, int type, int level, long exp, int leaderBonus, int strengthBonus, int intelBonus, int politicsBonus,
        int forces, int location, int state, int morale, int autoState) =>
        new(g.Id, g.Name, type, g.Pic, g.Quality, level, exp, g.Leader + leaderBonus, g.Strength + strengthBonus,
            g.Intel + intelBonus, g.Politics + politicsBonus, g.TroopId, g.TacticId, g.StratagemId, forces, location, state, morale, autoState);

    static int FirstFreePosition(IEnumerable<int> used)
    {
        var set = used.ToHashSet();
        for (var i = 1; i <= 5; i++) if (!set.Contains(i)) return i;
        return 5;
    }
    static HashSet<int> ParseIdSet(string value) => (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => int.TryParse(x, out var id) ? id : 0).Where(x => x > 0).ToHashSet();
    static double[] ParseDoubleList(string? value, double[] fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var result = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => double.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0d).ToArray();
        return result.Length == 0 ? fallback : result;
    }
    static void ValidateType(int type) { if (type is not (CivilType or MilitaryType)) throw new GameException("GENERAL_TYPE_INVALID", "Loại võ tướng không hợp lệ."); }

    sealed record PlayerRow(long Id, int Level, int ForceId, long SysGold, long UserGold, int ConsumeLevel);
    sealed record TavernRow(int State, int CivilRefreshTime, int MilitaryRefreshTime, DateTimeOffset NextCivilAt, DateTimeOffset NextMilitaryAt, string LockedGeneralIds);
    sealed record TavernOfferRow(int GeneralId, int Type, int Position, bool Locked, bool Bought, bool IsGold, int Price);
}

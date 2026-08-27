using System.Globalization;
using CTXD.Server.Data;
using CTXD.Server.Domain;
using CTXD.Server.Models;
using Npgsql;

namespace CTXD.Server.Services;

/// <summary>
/// Modern implementation of the legacy equipment-store slice (StoreService) used by the remake.
/// It intentionally ports only equipment styles 1/2; treasure/blueprint branches remain with their
/// owning future subsystems instead of being smuggled into this service.
/// </summary>
public sealed class EquipmentStoreService(
    GameDb db,
    CanonicalContent content,
    TutorialService tutorial,
    ResourceProductionService resources,
    DstqActivityService dstq)
{
    const int MilitaryStoreType = 1; // legacy equip goods_type 1..6
    const int CivilStoreType = 2;    // legacy equip goods_type 7..12
    const int MilitaryStoreFunction = 18; // verified StoreService functionId char[18]
    const int CivilStoreFunction = 17;    // verified StoreService functionId char[17]
    static readonly TimeSpan RefreshLeadLimit = TimeSpan.FromMinutes(30); // legacy check > 1,800,000ms
    static readonly TimeSpan MilitaryRefreshStep = TimeSpan.FromMinutes(3); // city-effect reduction deferred with World
    static readonly TimeSpan CivilRefreshStep = TimeSpan.FromSeconds(30);

    public async Task<StoreResponse> GetAsync(long playerId, int storeType, CancellationToken ct)
    {
        ValidateStoreType(storeType);
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var player = await ReadPlayerAsync(conn, tx, playerId, ct);
        await RequireOpenAsync(conn, tx, playerId, storeType, ct);
        var store = await EnsureStoreAsync(conn, tx, playerId, ct, forUpdate: true);

        var pending = storeType == MilitaryStoreType ? store.PendingStyle1 : store.PendingStyle2;
        var offers = await ReadOffersAsync(conn, tx, playerId, storeType, ct);
        if (pending || offers.Count == 0)
        {
            // Tutorial refresh_store_equip is the legacy refreshItem(playerId, 1, false) path.
            // It bypasses the user-facing level gate but still advances store state/CD.
            store = await AdvanceRefreshAsync(conn, tx, player, store, storeType, enforceLevelGate: false, ct);
            await GenerateOffersAsync(conn, tx, player, store, storeType, ct);
            await ClearPendingAsync(conn, tx, playerId, storeType, ct);
            offers = await ReadOffersAsync(conn, tx, playerId, storeType, ct);
        }

        var result = await BuildResponseAsync(conn, tx, player, store, storeType, offers, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<StoreResponse> RefreshAsync(long playerId, int storeType, CancellationToken ct)
    {
        ValidateStoreType(storeType);
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var player = await ReadPlayerAsync(conn, tx, playerId, ct, forUpdate: true);
        await RequireOpenAsync(conn, tx, playerId, storeType, ct);
        var store = await EnsureStoreAsync(conn, tx, playerId, ct, forUpdate: true);

        store = await AdvanceRefreshAsync(conn, tx, player, store, storeType, enforceLevelGate: true, ct);
        await GenerateOffersAsync(conn, tx, player, store, storeType, ct);
        await ClearPendingAsync(conn, tx, playerId, storeType, ct);
        await tutorial.TryCompleteAsync(conn, tx, playerId, "store_refresh", [], ct);

        var offers = await ReadOffersAsync(conn, tx, playerId, storeType, ct);
        var result = await BuildResponseAsync(conn, tx, player, store, storeType, offers, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<StoreResponse> SetLockedAsync(long playerId, int equipmentId, bool locked, CancellationToken ct)
    {
        if (!content.Equipment.TryGetValue(equipmentId, out var equip))
            throw new GameException("EQUIPMENT_NOT_FOUND", "Không có trang bị này.", 404);
        var storeType = StyleForGoodsType(equip.Type);
        if (storeType == 0) throw new GameException("STORE_ITEM_INVALID", "Vật phẩm không thuộc cửa hàng trang bị.");

        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var player = await ReadPlayerAsync(conn, tx, playerId, ct);
        await RequireOpenAsync(conn, tx, playerId, storeType, ct);
        var store = await EnsureStoreAsync(conn, tx, playerId, ct, forUpdate: true);

        await using (var cmd = new NpgsqlCommand(@"
UPDATE player_store_offers SET locked=$3
WHERE player_id=$1 AND equipment_id=$2 AND store_type=$4 AND bought=FALSE", conn, tx))
        {
            cmd.Parameters.AddWithValue(playerId);
            cmd.Parameters.AddWithValue(equipmentId);
            cmd.Parameters.AddWithValue(locked);
            cmd.Parameters.AddWithValue(storeType);
            if (await cmd.ExecuteNonQueryAsync(ct) == 0)
                throw new GameException("STORE_OFFER_MISSING", "Trang bị không còn trong cửa hàng.");
        }

        var offers = await ReadOffersAsync(conn, tx, playerId, storeType, ct);
        var result = await BuildResponseAsync(conn, tx, player, store, storeType, offers, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<BuyEquipmentResponse> BuyAsync(long playerId, int equipmentId, CancellationToken ct)
    {
        if (!content.Equipment.TryGetValue(equipmentId, out var equip))
            throw new GameException("EQUIPMENT_NOT_FOUND", "Không có trang bị này.", 404);
        var storeType = StyleForGoodsType(equip.Type);
        if (storeType == 0) throw new GameException("STORE_ITEM_INVALID", "Vật phẩm không thuộc cửa hàng trang bị.");

        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var player = await ReadPlayerAsync(conn, tx, playerId, ct, forUpdate: true);
        await RequireOpenAsync(conn, tx, playerId, storeType, ct);
        await resources.AccrueAndGetAsync(playerId, ct, conn, tx);

        OfferRow offer;
        await using (var cmd = new NpgsqlCommand(@"
SELECT position,equipment_type,locked,bought,is_gold,is_cheap,price,refresh_attribute
FROM player_store_offers
WHERE player_id=$1 AND store_type=$2 AND equipment_id=$3 FOR UPDATE", conn, tx))
        {
            cmd.Parameters.AddWithValue(playerId);
            cmd.Parameters.AddWithValue(storeType);
            cmd.Parameters.AddWithValue(equipmentId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) throw new GameException("ITEM_NOT_REFRESHED", "Trang bị chưa xuất hiện trong cửa hàng.");
            offer = new OfferRow(r.GetInt32(0), equipmentId, r.GetInt16(1), r.GetBoolean(2), r.GetBoolean(3),
                r.GetBoolean(4), r.GetBoolean(5), r.GetInt32(6), r.GetString(7));
        }
        if (offer.Bought) throw new GameException("ITEM_ALREADY_BOUGHT", "Trang bị đã được mua.");

        var nowItemNum = await CountInventoryAsync(conn, tx, playerId, ct);
        if (nowItemNum >= player.MaxStoreNum) throw new GameException("STORE_FULL", "Kho trang bị đã đầy.");

        if (offer.IsGold)
        {
            if (player.ConsumeLevel < 8)
                throw new GameException("GOLD_CONSUME_LOCKED", "Chưa đạt cấp tiêu phí để mua bằng Hoàng Kim.");
            var total = player.SysGold + player.UserGold;
            if (total < offer.Price) throw new GameException("GOLD_NOT_ENOUGH", "Hoàng kim không đủ.");
            var useUser = Math.Min(player.UserGold, offer.Price);
            var useSys = offer.Price - useUser;
            await using var pay = new NpgsqlCommand("UPDATE players SET user_gold=user_gold-$2,sys_gold=sys_gold-$3,updated_at=now() WHERE id=$1", conn, tx);
            pay.Parameters.AddWithValue(playerId);
            pay.Parameters.AddWithValue(useUser);
            pay.Parameters.AddWithValue(useSys);
            await pay.ExecuteNonQueryAsync(ct);
            await dstq.RecordGoldSpendAsync(conn,tx,playerId,offer.Price,ct);
        }
        else
        {
            await using var pay = new NpgsqlCommand("UPDATE player_resources SET copper=copper-$2 WHERE player_id=$1 AND copper >= $2", conn, tx);
            pay.Parameters.AddWithValue(playerId);
            pay.Parameters.AddWithValue(offer.Price);
            if (await pay.ExecuteNonQueryAsync(ct) == 0) throw new GameException("COPPER_NOT_ENOUGH", "Bạc không đủ.");
        }

        await using (var mark = new NpgsqlCommand(@"
UPDATE player_store_offers SET bought=TRUE WHERE player_id=$1 AND store_type=$2 AND equipment_id=$3", conn, tx))
        {
            mark.Parameters.AddWithValue(playerId); mark.Parameters.AddWithValue(storeType); mark.Parameters.AddWithValue(equipmentId);
            await mark.ExecuteNonQueryAsync(ct);
        }

        long instanceId;
        await using (var add = new NpgsqlCommand(@"
INSERT INTO player_equipment(player_id,equipment_id,goods_type,level,quality,attribute,owner_general_id,refresh_attribute,gem_id,quenching_times,state,num)
VALUES($1,$2,$3,$4,$5,$6,NULL,$7,0,0,0,1)
RETURNING id", conn, tx))
        {
            add.Parameters.AddWithValue(playerId);
            add.Parameters.AddWithValue(equipmentId);
            add.Parameters.AddWithValue(equip.Type);
            add.Parameters.AddWithValue(equip.DefaultLevel);
            add.Parameters.AddWithValue(equip.Quality);
            add.Parameters.AddWithValue(equip.Attribute);
            add.Parameters.AddWithValue(offer.RefreshAttribute ?? "");
            instanceId = Convert.ToInt64(await add.ExecuteScalarAsync(ct));
        }

        await tutorial.TryCompleteAsync(conn, tx, playerId, "equip", [], ct);
        var item = await ReadEquipmentAsync(conn, tx, playerId, instanceId, ct)
                   ?? throw new GameException("EQUIPMENT_CREATE_FAILED", "Không thể tạo trang bị.", 500);
        var res = await resources.AccrueAndGetAsync(playerId, ct, conn, tx);
        nowItemNum++;
        await tx.CommitAsync(ct);
        return new BuyEquipmentResponse(item, res, nowItemNum, player.MaxStoreNum);
    }

    async Task<StoreRow> AdvanceRefreshAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, PlayerRow player, StoreRow store,
        int storeType, bool enforceLevelGate, CancellationToken ct)
    {
        if (enforceLevelGate && player.Level < 18)
            throw new GameException("STORE_REFRESH_LEVEL", "Cấp 18 mới có thể tự làm mới cửa hàng.");

        var now = DateTimeOffset.UtcNow;
        var oldNext = storeType == MilitaryStoreType ? store.NextStyle1At : store.NextStyle2At;
        if (oldNext - now > RefreshLeadLimit)
            throw new GameException("STORE_REFRESH_CD", "Thời gian chờ làm mới cửa hàng đã đạt giới hạn.");

        var nextState = DecideNextState(store.State, content.MaxStoreQuality(player.Level, storeType));
        var baseTime = oldNext > now ? oldNext : now;
        var nextAt = baseTime + (storeType == MilitaryStoreType ? MilitaryRefreshStep : CivilRefreshStep);
        if (storeType == MilitaryStoreType)
        {
            await using var cmd = new NpgsqlCommand(@"
UPDATE player_store SET store_state=$2,style1_refresh_count=style1_refresh_count+1,next_style1_at=$3,updated_at=now()
WHERE player_id=$1", conn, tx);
            cmd.Parameters.AddWithValue(player.Id); cmd.Parameters.AddWithValue(nextState); cmd.Parameters.AddWithValue(nextAt);
            await cmd.ExecuteNonQueryAsync(ct);
            return store with { State = nextState, Style1RefreshCount = store.Style1RefreshCount + 1, NextStyle1At = nextAt };
        }
        else
        {
            await using var cmd = new NpgsqlCommand(@"
UPDATE player_store SET store_state=$2,style2_refresh_count=style2_refresh_count+1,next_style2_at=$3,updated_at=now()
WHERE player_id=$1", conn, tx);
            cmd.Parameters.AddWithValue(player.Id); cmd.Parameters.AddWithValue(nextState); cmd.Parameters.AddWithValue(nextAt);
            await cmd.ExecuteNonQueryAsync(ct);
            return store with { State = nextState, Style2RefreshCount = store.Style2RefreshCount + 1, NextStyle2At = nextAt };
        }
    }

    async Task GenerateOffersAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, PlayerRow player, StoreRow store, int storeType, CancellationToken ct)
    {
        var existing = await ReadOffersAsync(conn, tx, player.Id, storeType, ct);
        var kept = existing.Where(x => x.Locked && !x.Bought).ToList();
        if (kept.Count >= 6) return;

        var forcedIds = ParseLockedIds(store.LockedEquipmentIds);
        var occupiedTypes = kept.Select(x => x.GoodsType).ToHashSet();
        var newOffers = new List<OfferRow>(kept);

        foreach (var equipmentId in forcedIds)
        {
            if (newOffers.Count >= 6) break;
            if (!content.Equipment.TryGetValue(equipmentId, out var eq) || StyleForGoodsType(eq.Type) != storeType) continue;
            if (occupiedTypes.Contains(eq.Type)) continue;
            var pos = PositionForGoodsType(eq.Type, storeType);
            if (pos is < 1 or > 6) continue;
            newOffers.Add(CreateOffer(pos, eq, locked: true, RefreshCount(store, storeType)));
            occupiedTypes.Add(eq.Type);
        }

        for (var pos = 1; pos <= 6; pos++)
        {
            var goodsType = storeType == MilitaryStoreType ? pos : pos + 6;
            if (occupiedTypes.Contains(goodsType)) continue;
            var candidates = content.EquipmentAvailableForStoreType(player.Level, storeType)
                .Where(x => x.Type == goodsType)
                .Where(x => !content.StoreItemByEquipmentId.TryGetValue(x.Id, out var si) || si.MinRefreshTime <= RefreshCount(store, storeType))
                .ToArray();
            if (candidates.Length == 0) continue;
            var chosen = ChooseEquipment(candidates, player.Intimacy);
            newOffers.Add(CreateOffer(pos, chosen, locked: false, RefreshCount(store, storeType)));
            occupiedTypes.Add(goodsType);
        }

        await using (var del = new NpgsqlCommand("DELETE FROM player_store_offers WHERE player_id=$1 AND store_type=$2", conn, tx))
        { del.Parameters.AddWithValue(player.Id); del.Parameters.AddWithValue(storeType); await del.ExecuteNonQueryAsync(ct); }

        foreach (var o in newOffers.OrderBy(x => x.Position).Take(6))
        {
            await using var ins = new NpgsqlCommand(@"
INSERT INTO player_store_offers(player_id,store_type,position,equipment_id,equipment_type,locked,bought,is_gold,is_cheap,price,refresh_attribute)
VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)
ON CONFLICT(player_id,store_type,position) DO UPDATE SET
 equipment_id=EXCLUDED.equipment_id,equipment_type=EXCLUDED.equipment_type,locked=EXCLUDED.locked,
 bought=EXCLUDED.bought,is_gold=EXCLUDED.is_gold,is_cheap=EXCLUDED.is_cheap,price=EXCLUDED.price,
 refresh_attribute=EXCLUDED.refresh_attribute,created_at=now()", conn, tx);
            ins.Parameters.AddWithValue(player.Id); ins.Parameters.AddWithValue(storeType); ins.Parameters.AddWithValue(o.Position);
            ins.Parameters.AddWithValue(o.EquipmentId); ins.Parameters.AddWithValue(o.GoodsType); ins.Parameters.AddWithValue(o.Locked);
            ins.Parameters.AddWithValue(o.Bought); ins.Parameters.AddWithValue(o.IsGold); ins.Parameters.AddWithValue(o.IsCheap);
            ins.Parameters.AddWithValue(o.Price); ins.Parameters.AddWithValue(o.RefreshAttribute ?? "");
            await ins.ExecuteNonQueryAsync(ct);
        }

        if (player.Level >= 18 && content.IntimacyLevel(player.Intimacy) < content.MaxIntimacyLevelForPlayer(player.Level))
        {
            const int legacyHardMax = 48_511_100;
            var nextIntimacy = Math.Min(legacyHardMax, player.Intimacy <= 0 ? 1 : player.Intimacy + 1);
            await using var intimacy = new NpgsqlCommand(
                "UPDATE players SET intimacy=$2,updated_at=now() WHERE id=$1", conn, tx);
            intimacy.Parameters.AddWithValue(player.Id);
            intimacy.Parameters.AddWithValue(nextIntimacy);
            await intimacy.ExecuteNonQueryAsync(ct);
        }
    }

    OfferRow CreateOffer(int position, EquipmentDefinition eq, bool locked, int refreshCount)
    {
        content.StoreItemByEquipmentId.TryGetValue(eq.Id, out var staticItem);
        var isGold = staticItem is not null && staticItem.GoldProbability > 0 && Random.Shared.NextDouble() <= staticItem.GoldProbability;
        var basePrice = isGold ? staticItem!.Gold : (staticItem?.Copper ?? eq.CopperBuy);
        var (factor, cheap) = DecidePriceFactor();
        var price = Math.Max(0, (int)Math.Round(basePrice * factor, MidpointRounding.AwayFromZero));
        var refreshAttribute = EquipmentSkillEffectService.GenerateRefreshAttribute(content, eq);
        return new OfferRow(position, eq.Id, eq.Type, locked, false, isGold, cheap, price, refreshAttribute);
    }

    EquipmentDefinition ChooseEquipment(IReadOnlyList<EquipmentDefinition> source, int intimacy)
    {
        var sorted = source.OrderBy(x => x.IntimacyGroup).ThenBy(x => x.Id).ToArray();
        if (sorted.Length == 1) return sorted[0];
        var first = sorted[0];
        var topGroup = sorted.Where(x => x.IntimacyGroup == first.IntimacyGroup).ToArray();
        var p = Math.Clamp(first.ProbBase + intimacy * first.ProbIntimacy, 0d, 1d);
        if (Random.Shared.NextDouble() <= p && topGroup.Length > 0)
        {
            var total = topGroup.Sum(x => Math.Max(0d, x.IntimacyGroupProb));
            if (total <= 0) return topGroup[Random.Shared.Next(topGroup.Length)];
            var roll = Random.Shared.NextDouble() * total;
            var acc = 0d;
            foreach (var e in topGroup)
            {
                acc += Math.Max(0d, e.IntimacyGroupProb);
                if (roll <= acc) return e;
            }
            return topGroup[^1];
        }
        var lower = sorted.Skip(topGroup.Length).ToArray();
        var pool = lower.Length > 0 ? lower : sorted;
        return pool[Random.Shared.Next(pool.Length)];
    }

    (double Factor, bool Cheap) DecidePriceFactor()
    {
        var factors = ParseDoubles(content.StringConstants.TryGetValue(1, out var a) ? a.Value : "1");
        var probs = ParseDoubles(content.StringConstants.TryGetValue(2, out var b) ? b.Value : "1");
        if (factors.Length == 0) return (1d, true);
        if (probs.Length != factors.Length) return (factors[0], true);
        var total = probs.Sum(x => Math.Max(0, x));
        if (total <= 0) return (factors[0], true);
        var roll = Random.Shared.NextDouble() * total;
        var acc = 0d;
        for (var i = 0; i < probs.Length; i++)
        {
            acc += Math.Max(0, probs[i]);
            if (roll <= acc) return (factors[i], i == 0);
        }
        return (factors[^1], factors.Length == 1);
    }

    int DecideNextState(int currentState, int maxQuality)
    {
        var all = content.StoreTransitionsFrom(currentState).Take(Math.Max(1, maxQuality)).ToArray();
        if (all.Length == 0) return currentState;
        var total = all.Sum(x => Math.Max(0d, x.Probability));
        if (total <= 0) return all[0].NextState;
        var roll = Random.Shared.NextDouble() * total;
        var acc = 0d;
        foreach (var t in all)
        {
            acc += Math.Max(0d, t.Probability);
            if (roll <= acc) return t.NextState;
        }
        return all[^1].NextState;
    }

    async Task<StoreResponse> BuildResponseAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, PlayerRow player, StoreRow store, int storeType,
        IReadOnlyList<OfferRow> offers, CancellationToken ct)
    {
        var views = new List<StoreOfferView>();
        foreach (var o in offers.OrderBy(x => x.Position))
        {
            if (!content.Equipment.TryGetValue(o.EquipmentId, out var e)) continue;
            views.Add(new StoreOfferView(o.Position, e.Id, e.Name, e.Pic, e.Quality, e.Type, e.DefaultLevel,
                o.Locked, o.Bought, o.IsGold, o.IsCheap, o.Price, e.Attribute, o.RefreshAttribute));
        }
        var now = await CountInventoryAsync(conn, tx, player.Id, ct);
        int intimacy;
        await using (var q = new NpgsqlCommand("SELECT intimacy FROM players WHERE id=$1", conn, tx))
        {
            q.Parameters.AddWithValue(player.Id);
            intimacy = Convert.ToInt32(await q.ExecuteScalarAsync(ct));
        }
        return new StoreResponse(storeType, store.State, RefreshCount(store, storeType), NextAt(store, storeType), intimacy,
            now, player.MaxStoreNum, content.MaxStoreQuality(player.Level, storeType), views);
    }

    async Task<List<OfferRow>> ReadOffersAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, int type, CancellationToken ct)
    {
        var list = new List<OfferRow>();
        await using var cmd = new NpgsqlCommand(@"
SELECT position,equipment_id,equipment_type,locked,bought,is_gold,is_cheap,price,refresh_attribute
FROM player_store_offers WHERE player_id=$1 AND store_type=$2 ORDER BY position", conn, tx);
        cmd.Parameters.AddWithValue(playerId); cmd.Parameters.AddWithValue(type);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new OfferRow(r.GetInt32(0), r.GetInt32(1), r.GetInt16(2), r.GetBoolean(3), r.GetBoolean(4),
                r.GetBoolean(5), r.GetBoolean(6), r.GetInt32(7), r.GetString(8)));
        return list;
    }

    async Task<PlayerEquipmentView?> ReadEquipmentAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, long instanceId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(@"
SELECT id,equipment_id,goods_type,level,quality,attribute,owner_general_id,refresh_attribute,gem_id,quenching_times,state,num
FROM player_equipment WHERE player_id=$1 AND id=$2", conn, tx);
        cmd.Parameters.AddWithValue(playerId); cmd.Parameters.AddWithValue(instanceId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var equipmentId = r.GetInt32(1);
        if (!content.Equipment.TryGetValue(equipmentId, out var e)) return null;
        return new PlayerEquipmentView(r.GetInt64(0), equipmentId, e.Name, e.Pic, r.GetInt32(2), r.GetInt32(4), r.GetInt32(3),
            r.GetInt32(5), r.IsDBNull(6) ? null : r.GetInt32(6), r.GetString(7), r.GetInt32(8), r.GetInt32(9),
            r.GetInt32(10), r.GetInt32(11), e.CopperSold);
    }

    async Task<int> CountInventoryAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM player_equipment WHERE player_id=$1", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    async Task RequireOpenAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, int storeType, CancellationToken ct)
    {
        var functionId = storeType == MilitaryStoreType ? MilitaryStoreFunction : CivilStoreFunction;
        await using var cmd = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM player_functions WHERE player_id=$1 AND function_id=$2)", conn, tx);
        cmd.Parameters.AddWithValue(playerId); cmd.Parameters.AddWithValue(functionId);
        if (!(bool)(await cmd.ExecuteScalarAsync(ct))!) throw new GameException("STORE_LOCKED", "Chưa mở cửa hàng trang bị.");
    }

    async Task<StoreRow> EnsureStoreAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, CancellationToken ct, bool forUpdate)
    {
        await using (var ensure = new NpgsqlCommand("INSERT INTO player_store(player_id) VALUES($1) ON CONFLICT DO NOTHING", conn, tx))
        { ensure.Parameters.AddWithValue(playerId); await ensure.ExecuteNonQueryAsync(ct); }
        var sql = @"SELECT store_state,style1_refresh_count,style2_refresh_count,next_style1_at,next_style2_at,
locked_equipment_ids,pending_refresh_style1,pending_refresh_style2 FROM player_store WHERE player_id=$1" + (forUpdate ? " FOR UPDATE" : "");
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) throw new GameException("STORE_STATE_MISSING", "Thiếu trạng thái cửa hàng.", 500);
        return new StoreRow(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetFieldValue<DateTimeOffset>(3),
            r.GetFieldValue<DateTimeOffset>(4), r.GetString(5), r.GetBoolean(6), r.GetBoolean(7));
    }

    async Task<PlayerRow> ReadPlayerAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, CancellationToken ct, bool forUpdate = false)
    {
        await using var cmd = new NpgsqlCommand(@"SELECT id,level,sys_gold,user_gold,consume_level,max_store_num,intimacy
FROM players WHERE id=$1" + (forUpdate ? " FOR UPDATE" : ""), conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy nhân vật.", 404);
        return new PlayerRow(r.GetInt64(0), r.GetInt32(1), r.GetInt64(2), r.GetInt64(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6));
    }

    static async Task ClearPendingAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long playerId, int storeType, CancellationToken ct)
    {
        var column = storeType == MilitaryStoreType ? "pending_refresh_style1" : "pending_refresh_style2";
        await using var cmd = new NpgsqlCommand($"UPDATE player_store SET {column}=FALSE,updated_at=now() WHERE player_id=$1", conn, tx);
        cmd.Parameters.AddWithValue(playerId); await cmd.ExecuteNonQueryAsync(ct);
    }

    static HashSet<int> ParseLockedIds(string raw)
    {
        var set = new HashSet<int>();
        foreach (var part in (raw ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(part, out var id) && id > 0) set.Add(id);
        return set;
    }

    static double[] ParseDoubles(string raw) => (raw ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0d).ToArray();

    static int StyleForGoodsType(int goodsType) => goodsType switch { >= 1 and <= 6 => 1, >= 7 and <= 12 => 2, _ => 0 };
    static int PositionForGoodsType(int goodsType, int storeType) => storeType == MilitaryStoreType ? goodsType : goodsType - 6;
    static int RefreshCount(StoreRow s, int type) => type == MilitaryStoreType ? s.Style1RefreshCount : s.Style2RefreshCount;
    static DateTimeOffset NextAt(StoreRow s, int type) => type == MilitaryStoreType ? s.NextStyle1At : s.NextStyle2At;
    static void ValidateStoreType(int type) { if (type is not (1 or 2)) throw new GameException("STORE_TYPE_INVALID", "Loại cửa hàng không hợp lệ."); }

    sealed record StoreRow(int State, int Style1RefreshCount, int Style2RefreshCount, DateTimeOffset NextStyle1At,
        DateTimeOffset NextStyle2At, string LockedEquipmentIds, bool PendingStyle1, bool PendingStyle2);
    sealed record PlayerRow(long Id, int Level, long SysGold, long UserGold, int ConsumeLevel, int MaxStoreNum, int Intimacy);
    sealed record OfferRow(int Position, int EquipmentId, int GoodsType, bool Locked, bool Bought, bool IsGold,
        bool IsCheap, int Price, string RefreshAttribute);
}

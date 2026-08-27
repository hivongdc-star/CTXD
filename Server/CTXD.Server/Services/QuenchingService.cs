using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using CTXD.Server.Data;
using CTXD.Server.Domain;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record QuenchingPlayerState(
    int FreeQuenchingTimes,
    int FreeNiubiQuenchingTimes,
    int Remind,
    int PaidGoldCost);

public sealed record QuenchingEquipmentState(
    long InstanceId,
    int EquipmentId,
    string RefreshAttribute,
    int QuenchingTimes,
    int QuenchingTimesFree,
    int SpecialSkillId,
    int MaxSkillLevel,
    bool IsJinpin,
    bool IsFull);

public sealed record QuenchingView(
    QuenchingPlayerState Player,
    QuenchingEquipmentState Equipment,
    bool Tech45Open);

public sealed record QuenchingResult(
    string Mode,
    bool UsedFreeNiubi,
    int GoldSpent,
    int InternalAttempts,
    bool SpecialCreated,
    QuenchingPlayerState Player,
    QuenchingEquipmentState Equipment);

/// <summary>
/// Server-authoritative legacy QuenChingService slice.
/// Paid quenching consumes chargeitem 36 through the legacy PlayerDao.consumeGold priority:
/// sys_gold first, then user_gold. A generated special skill is already appended as a duplicate
/// token in refresh_attribute; battle must not add special_skill_id as another combat effect.
/// </summary>
public sealed class QuenchingService(
    GameDb db,
    CanonicalContent content,
    TechnologyEffectService technology,
    DstqActivityService dstq)
{
    const int QuenchingFunctionId = 51;
    const int QuenchingChargeItemId = 36;
    const int SpecialSkillTechKey = 45;

    enum QuenchingMode
    {
        Paid = 1,
        Free = 2
    }

    sealed class SkillDef
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int Id { get; set; }

        [JsonPropertyName("skill_type"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int SkillType { get; set; }
    }

    sealed class SkillLevelDef
    {
        [JsonPropertyName("lv"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int Level { get; set; }

        [JsonPropertyName("upgrade_prob_gold"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public double UpgradeProbabilityGold { get; set; }

        [JsonPropertyName("upgrade_max_times_gold"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int UpgradeMaxTimesGold { get; set; }

        [JsonPropertyName("upgrade_prob_free"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public double UpgradeProbabilityFree { get; set; }

        [JsonPropertyName("upgrade_max_times_free"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int UpgradeMaxTimesFree { get; set; }
    }

    sealed record Catalog(
        IReadOnlyDictionary<int, SkillDef[]> SkillsByType,
        IReadOnlyDictionary<int, SkillLevelDef> Levels,
        IReadOnlySet<int> JinpinEquipmentIds);

    sealed record PlayerStateRow(int FreeQuenchingTimes, int FreeNiubiQuenchingTimes, int Remind);

    sealed record EquipmentRow(
        long InstanceId,
        int EquipmentId,
        string RefreshAttribute,
        int QuenchingTimes,
        int QuenchingTimesFree,
        int SpecialSkillId);

    readonly record struct SkillToken(int SkillId, int Level)
    {
        public override string ToString() => $"{SkillId}:{Level}";
    }

    sealed record ParsedRefresh(SkillToken[] AllTokens, SkillToken[] OrdinaryTokens);

    static readonly ConcurrentDictionary<string, Catalog> CatalogCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<QuenchingView> GetAsync(long playerId, long instanceId, CancellationToken ct)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await RequireOpenAsync(conn, null, playerId, ct);

        var state = await ReadPlayerStateAsync(conn, null, playerId, ensure: false, forUpdate: false, ct);
        var equipment = await ReadEquipmentAsync(conn, null, playerId, instanceId, forUpdate: false, ct);
        var definition = GetEquipmentDefinition(equipment.EquipmentId);
        var catalog = GetCatalog();
        var parsed = ParseRefreshAttribute(equipment.RefreshAttribute, definition, catalog);
        var jinpin = catalog.JinpinEquipmentIds.Contains(definition.Id);
        var full = IsFullForRetry(parsed.AllTokens, definition, jinpin);
        var tech45Open = await IsSpecialSkillTechOpenAsync(conn, null, playerId, ct);

        return new QuenchingView(
            BuildPlayerState(state),
            BuildEquipmentState(equipment, definition, jinpin, full),
            tech45Open);
    }

    public Task<QuenchingResult> PaidAsync(long playerId, long instanceId, CancellationToken ct) =>
        QuenchAsync(playerId, instanceId, QuenchingMode.Paid, ct);

    public Task<QuenchingResult> FreeAsync(long playerId, long instanceId, CancellationToken ct) =>
        QuenchAsync(playerId, instanceId, QuenchingMode.Free, ct);

    async Task<QuenchingResult> QuenchAsync(long playerId, long instanceId, QuenchingMode mode, CancellationToken ct)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await RequireOpenAsync(conn, tx, playerId, ct);
        var playerState = await ReadPlayerStateAsync(conn, tx, playerId, ensure: true, forUpdate: true, ct);
        var equipment = await ReadEquipmentAsync(conn, tx, playerId, instanceId, forUpdate: true, ct);
        var definition = GetEquipmentDefinition(equipment.EquipmentId);
        var catalog = GetCatalog();
        var parsed = ParseRefreshAttribute(equipment.RefreshAttribute, definition, catalog);

        var usedFreeNiubi = false;
        var goldSpent = 0;
        if (mode == QuenchingMode.Free)
        {
            if (playerState.FreeQuenchingTimes <= 0)
                throw new GameException("QUENCHING_FREE_TIMES_EMPTY", "Không còn lượt tẩy luyện miễn phí.");
            playerState = playerState with { FreeQuenchingTimes = playerState.FreeQuenchingTimes - 1 };
        }
        else if (playerState.FreeNiubiQuenchingTimes > 0)
        {
            usedFreeNiubi = true;
            playerState = playerState with { FreeNiubiQuenchingTimes = playerState.FreeNiubiQuenchingTimes - 1 };
        }
        else
        {
            goldSpent = GetPaidGoldCost();
            await ConsumeGoldLegacyAsync(conn, tx, playerId, goldSpent, ct);
            await dstq.RecordGoldSpendAsync(conn, tx, playerId, goldSpent, ct);
        }

        var jinpin = catalog.JinpinEquipmentIds.Contains(definition.Id);
        var full = IsFullForRetry(parsed.AllTokens, definition, jinpin);
        var maxAttempts = full ? (mode == QuenchingMode.Paid ? 4 : 2) : 1;
        var counter = mode == QuenchingMode.Paid ? equipment.QuenchingTimes : equipment.QuenchingTimesFree;
        var tech45Open = await IsSpecialSkillTechOpenAsync(conn, tx, playerId, ct);

        var finalOrdinary = parsed.OrdinaryTokens;
        var finalRefreshAttribute = string.Join(';', finalOrdinary.Select(x => x.ToString()));
        var specialCreated = false;
        var specialSkillId = equipment.SpecialSkillId;
        var attemptsUsed = 0;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            attemptsUsed++;
            var rolled = new SkillToken[parsed.OrdinaryTokens.Length];
            for (var i = 0; i < parsed.OrdinaryTokens.Length; i++)
            {
                var current = parsed.OrdinaryTokens[i];
                if (!catalog.Levels.TryGetValue(current.Level, out var levelDef))
                    throw new GameException("QUENCHING_SKILL_LEVEL_MISSING", $"Thiếu equip_skill_lv cho level {current.Level}.", 500);

                var threshold = mode == QuenchingMode.Paid
                    ? levelDef.UpgradeMaxTimesGold
                    : levelDef.UpgradeMaxTimesFree;
                var probability = mode == QuenchingMode.Paid
                    ? levelDef.UpgradeProbabilityGold
                    : levelDef.UpgradeProbabilityFree;

                if (threshold > 0 && counter >= threshold && current.Level < definition.SkillLevelMax)
                {
                    rolled[i] = current with { Level = current.Level + 1 };
                    continue;
                }

                if (Random.Shared.NextDouble() < probability && current.Level < definition.SkillLevelMax)
                {
                    rolled[i] = current with { Level = current.Level + 1 };
                    continue;
                }

                if (!catalog.SkillsByType.TryGetValue(definition.SkillType, out var candidates) || candidates.Length == 0)
                    throw new GameException("QUENCHING_SKILL_TYPE_MISSING", $"Thiếu equip_skill cho skill_type={definition.SkillType}.", 500);
                var rerolled = candidates[Random.Shared.Next(candidates.Length)];
                rolled[i] = new SkillToken(rerolled.Id, current.Level);
            }

            finalOrdinary = rolled;
            finalRefreshAttribute = string.Join(';', rolled.Select(x => x.ToString()));
            if (!CanCreateSpecialSkill(tech45Open, definition, jinpin, rolled))
                continue;

            specialCreated = true;
            specialSkillId = rolled[0].SkillId;
            finalRefreshAttribute = $"{finalRefreshAttribute};{rolled[0]}";
            break;
        }

        await SaveEquipmentResultAsync(
            conn,
            tx,
            equipment.InstanceId,
            finalRefreshAttribute,
            mode,
            specialCreated,
            specialSkillId,
            ct);
        await SavePlayerStateAsync(conn, tx, playerId, playerState, ct);

        // TaskRequestQuenching is emitted once per successful operation, never per internal retry.
        await QuestEventLedger.RecordCurrentAsync(conn, tx, playerId, "equip_skill_refresh", 0, ct);

        var nextEquipment = equipment with
        {
            RefreshAttribute = finalRefreshAttribute,
            QuenchingTimes = equipment.QuenchingTimes + (mode == QuenchingMode.Paid ? 1 : 0),
            QuenchingTimesFree = equipment.QuenchingTimesFree + (mode == QuenchingMode.Free ? 1 : 0),
            // Legacy updateRefreshAttribute leaves SPECIAL_SKILL_ID untouched when no new special is created.
            SpecialSkillId = specialSkillId
        };
        var nextParsed = ParseRefreshAttribute(nextEquipment.RefreshAttribute, definition, catalog);
        var nextFull = IsFullForRetry(nextParsed.AllTokens, definition, jinpin);

        var result = new QuenchingResult(
            mode == QuenchingMode.Paid ? "paid" : "free",
            usedFreeNiubi,
            goldSpent,
            attemptsUsed,
            specialCreated,
            BuildPlayerState(playerState),
            BuildEquipmentState(nextEquipment, definition, jinpin, nextFull));

        await tx.CommitAsync(ct);
        return result;
    }

    async Task RequireOpenAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, long playerId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM player_functions WHERE player_id=$1 AND function_id=$2)", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        cmd.Parameters.AddWithValue(QuenchingFunctionId);
        if (!Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)))
            throw new GameException("QUENCHING_NOT_OPEN", "Tẩy luyện trang bị chưa được mở.", 403);
    }

    async Task<PlayerStateRow> ReadPlayerStateAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        long playerId,
        bool ensure,
        bool forUpdate,
        CancellationToken ct)
    {
        if (ensure)
        {
            await using var create = new NpgsqlCommand(
                "INSERT INTO player_quenching_state(player_id) VALUES($1) ON CONFLICT DO NOTHING", conn, tx);
            create.Parameters.AddWithValue(playerId);
            await create.ExecuteNonQueryAsync(ct);
        }

        await using var cmd = new NpgsqlCommand($@"
SELECT free_quenching_times,free_niubi_quenching_times,remind
FROM player_quenching_state
WHERE player_id=$1{(forUpdate ? " FOR UPDATE" : "")}", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (await r.ReadAsync(ct))
            return new PlayerStateRow(r.GetInt32(0), r.GetInt32(1), r.GetInt16(2));
        if (!ensure) return new PlayerStateRow(0, 0, 0);
        throw new GameException("QUENCHING_STATE_MISSING", "Không thể tạo trạng thái tẩy luyện.", 500);
    }

    static async Task SavePlayerStateAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long playerId,
        PlayerStateRow state,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(@"
UPDATE player_quenching_state
SET free_quenching_times=$2,free_niubi_quenching_times=$3,remind=$4,updated_at=now()
WHERE player_id=$1", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        cmd.Parameters.AddWithValue(state.FreeQuenchingTimes);
        cmd.Parameters.AddWithValue(state.FreeNiubiQuenchingTimes);
        cmd.Parameters.AddWithValue(state.Remind);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1)
            throw new GameException("QUENCHING_STATE_CHANGED", "Trạng thái tẩy luyện đã thay đổi.", 409);
    }

    static async Task<EquipmentRow> ReadEquipmentAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        long playerId,
        long instanceId,
        bool forUpdate,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($@"
SELECT id,equipment_id,refresh_attribute,quenching_times,quenching_times_free,special_skill_id
FROM player_equipment
WHERE player_id=$1 AND id=$2 AND num>0{(forUpdate ? " FOR UPDATE" : "")}", conn, tx);
        cmd.Parameters.AddWithValue(playerId);
        cmd.Parameters.AddWithValue(instanceId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            throw new GameException("EQUIPMENT_NOT_FOUND", "Không tìm thấy trang bị.", 404);
        return new EquipmentRow(
            r.GetInt64(0),
            r.GetInt32(1),
            r.IsDBNull(2) ? "" : r.GetString(2),
            r.GetInt32(3),
            r.GetInt32(4),
            r.GetInt32(5));
    }

    static async Task SaveEquipmentResultAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long instanceId,
        string refreshAttribute,
        QuenchingMode mode,
        bool specialCreated,
        int specialSkillId,
        CancellationToken ct)
    {
        var counterColumn = mode == QuenchingMode.Paid ? "quenching_times" : "quenching_times_free";
        var specialSql = specialCreated ? ",special_skill_id=$3" : "";
        await using var cmd = new NpgsqlCommand($@"
UPDATE player_equipment
SET refresh_attribute=$2,{counterColumn}={counterColumn}+1{specialSql},updated_at=now()
WHERE id=$1", conn, tx);
        cmd.Parameters.AddWithValue(instanceId);
        cmd.Parameters.AddWithValue(refreshAttribute);
        if (specialCreated) cmd.Parameters.AddWithValue(specialSkillId);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1)
            throw new GameException("EQUIPMENT_STATE_CHANGED", "Trang bị đã thay đổi trong lúc tẩy luyện.", 409);
    }

    async Task ConsumeGoldLegacyAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long playerId,
        int cost,
        CancellationToken ct)
    {
        long sysGold;
        long userGold;
        await using (var cmd = new NpgsqlCommand(
                         "SELECT sys_gold,user_gold FROM players WHERE id=$1 FOR UPDATE", conn, tx))
        {
            cmd.Parameters.AddWithValue(playerId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
                throw new GameException("PLAYER_NOT_FOUND", "Không tìm thấy nhân vật.", 404);
            sysGold = r.GetInt64(0);
            userGold = r.GetInt64(1);
        }

        if (sysGold + userGold < cost)
            throw new GameException("GOLD_NOT_ENOUGH", "Hoàng kim không đủ.");

        var newSysGold = sysGold;
        var newUserGold = userGold;
        if (sysGold >= cost)
        {
            newSysGold -= cost;
        }
        else
        {
            var remainder = cost - sysGold;
            newSysGold = 0;
            newUserGold -= remainder;
        }

        await using var update = new NpgsqlCommand(
            "UPDATE players SET sys_gold=$2,user_gold=$3,updated_at=now() WHERE id=$1", conn, tx);
        update.Parameters.AddWithValue(playerId);
        update.Parameters.AddWithValue(newSysGold);
        update.Parameters.AddWithValue(newUserGold);
        if (await update.ExecuteNonQueryAsync(ct) != 1)
            throw new GameException("PLAYER_STATE_CHANGED", "Tài nguyên người chơi đã thay đổi.", 409);
    }

    EquipmentDefinition GetEquipmentDefinition(int equipmentId) =>
        content.Equipment.TryGetValue(equipmentId, out var definition)
            ? definition
            : throw new GameException("EQUIPMENT_DEFINITION_MISSING", "Thiếu dữ liệu trang bị.", 500);

    int GetPaidGoldCost()
    {
        if (!content.ChargeItems.TryGetValue(QuenchingChargeItemId, out var chargeItem))
            throw new GameException("QUENCHING_CHARGEITEM_MISSING", "Thiếu chargeitem 36 cho tẩy luyện.", 500);
        if (chargeItem.Cost < 0)
            throw new GameException("QUENCHING_CHARGEITEM_INVALID", "Chi phí tẩy luyện không hợp lệ.", 500);
        return chargeItem.Cost;
    }

    async Task<bool> IsSpecialSkillTechOpenAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        long playerId,
        CancellationToken ct) =>
        await technology.GetCompletedIntEffectAsync(playerId, SpecialSkillTechKey, 0, ct, conn, tx) > 0;

    QuenchingPlayerState BuildPlayerState(PlayerStateRow state) =>
        new(state.FreeQuenchingTimes, state.FreeNiubiQuenchingTimes, state.Remind, GetPaidGoldCost());

    static QuenchingEquipmentState BuildEquipmentState(
        EquipmentRow equipment,
        EquipmentDefinition definition,
        bool jinpin,
        bool full) =>
        new(
            equipment.InstanceId,
            equipment.EquipmentId,
            equipment.RefreshAttribute,
            equipment.QuenchingTimes,
            equipment.QuenchingTimesFree,
            equipment.SpecialSkillId,
            definition.SkillLevelMax,
            jinpin,
            full);

    static bool IsFullForRetry(SkillToken[] allTokens, EquipmentDefinition definition, bool jinpin)
    {
        // Exact legacy EquipCommon.isFullLvAndNumber: quality >=5 non-Jinpin never enters retry mode.
        if (definition.Quality >= 5 && !jinpin) return false;
        if (allTokens.Length < definition.SkillNum) return false;
        return allTokens.All(x => x.Level == definition.SkillLevelMax);
    }

    static bool CanCreateSpecialSkill(
        bool tech45Open,
        EquipmentDefinition definition,
        bool jinpin,
        SkillToken[] ordinary)
    {
        if (!tech45Open || definition.Quality < 5 || !jinpin) return false;
        if (ordinary.Length < definition.SkillNum || definition.SkillNum <= 0) return false;
        if (ordinary.Any(x => x.Level != definition.SkillLevelMax)) return false;
        var skillId = ordinary[0].SkillId;
        return ordinary.All(x => x.SkillId == skillId);
    }

    static ParsedRefresh ParseRefreshAttribute(
        string raw,
        EquipmentDefinition definition,
        Catalog catalog)
    {
        if (definition.SkillNum <= 0 || string.IsNullOrWhiteSpace(raw))
            throw new GameException("QUENCHING_EQUIPMENT_INVALID", "Trang bị không có kỹ năng để tẩy luyện.");

        var parts = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Legacy runtime produces exactly skillNum ordinary tokens and optionally one appended special token.
        if (parts.Length != definition.SkillNum && parts.Length != definition.SkillNum + 1)
            throw new GameException("QUENCHING_REFRESH_ATTRIBUTE_INVALID", "Dữ liệu kỹ năng trang bị không hợp lệ.", 409);

        var all = new SkillToken[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            all[i] = ParseToken(parts[i]);

        if (!catalog.SkillsByType.TryGetValue(definition.SkillType, out var allowed) || allowed.Length == 0)
            throw new GameException("QUENCHING_SKILL_TYPE_MISSING", $"Thiếu equip_skill cho skill_type={definition.SkillType}.", 500);
        var allowedIds = allowed.Select(x => x.Id).ToHashSet();
        var ordinary = all.Take(definition.SkillNum).ToArray();
        foreach (var token in ordinary)
        {
            if (!allowedIds.Contains(token.SkillId))
                throw new GameException("QUENCHING_REFRESH_ATTRIBUTE_INVALID", "Kỹ năng trang bị không thuộc đúng skill_type.", 409);
            if (token.Level <= 0 || token.Level > definition.SkillLevelMax)
                throw new GameException("QUENCHING_REFRESH_ATTRIBUTE_INVALID", "Cấp kỹ năng trang bị không hợp lệ.", 409);
            if (!catalog.Levels.ContainsKey(token.Level))
                throw new GameException("QUENCHING_SKILL_LEVEL_MISSING", $"Thiếu equip_skill_lv cho level {token.Level}.", 500);
        }
        return new ParsedRefresh(all, ordinary);
    }

    static SkillToken ParseToken(string raw)
    {
        var pair = raw.Split(':', 2, StringSplitOptions.TrimEntries);
        if (pair.Length != 2 ||
            !int.TryParse(pair[0], out var skillId) || skillId <= 0 ||
            !int.TryParse(pair[1], out var level) || level <= 0)
            throw new GameException("QUENCHING_REFRESH_ATTRIBUTE_INVALID", "Dữ liệu kỹ năng trang bị không hợp lệ.", 409);
        return new SkillToken(skillId, level);
    }

    Catalog GetCatalog() => CatalogCache.GetOrAdd(content.BaseDirectory, LoadCatalog);

    static Catalog LoadCatalog(string directory)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        var skills = JsonSerializer.Deserialize<SkillDef[]>(
                         File.ReadAllText(Path.Combine(directory, "equip_skill.json")), options)
                     ?? throw new InvalidOperationException("Cannot load equip_skill.json.");
        var levels = JsonSerializer.Deserialize<SkillLevelDef[]>(
                         File.ReadAllText(Path.Combine(directory, "equip_skill_lv.json")), options)
                     ?? throw new InvalidOperationException("Cannot load equip_skill_lv.json.");
        var equipment = JsonSerializer.Deserialize<EquipmentDefinition[]>(
                            File.ReadAllText(Path.Combine(directory, "equipment.json")), options)
                        ?? throw new InvalidOperationException("Cannot load equipment.json.");

        var bestByTypeAndQuality = new Dictionary<(int Type, int Quality), EquipmentDefinition>();
        foreach (var item in equipment)
        {
            if (item.Quality < 4) continue;
            var key = (item.Type, item.Quality);
            // Legacy EquipCache replaces only on strictly larger skillNum; ties retain canonical first item.
            if (!bestByTypeAndQuality.TryGetValue(key, out var current) || item.SkillNum > current.SkillNum)
                bestByTypeAndQuality[key] = item;
        }

        return new Catalog(
            skills.GroupBy(x => x.SkillType).ToDictionary(x => x.Key, x => x.ToArray()),
            levels.ToDictionary(x => x.Level),
            bestByTypeAndQuality.Values.Select(x => x.Id).ToHashSet());
    }
}

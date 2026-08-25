using System.Text.Json;
using CTXD.Server.Domain;

namespace CTXD.Server.Data;

public sealed class CanonicalContent
{
    private readonly Dictionary<int, BuildingDefinition> _buildings;
    private readonly Dictionary<int, TaskDefinition> _tasks;
    private readonly Dictionary<int, Dictionary<int, int>> _serial;
    public IReadOnlyDictionary<int, BuildingDefinition> Buildings => _buildings;
    public IReadOnlyDictionary<int, TaskDefinition> Tasks => _tasks;
    public IReadOnlyDictionary<int,MarketProductDefinition> MarketProducts { get; }
    public IReadOnlyList<MarketDegreeDefinition> MarketDegrees { get; }
    public IReadOnlyDictionary<int,MarketIronDefinition> MarketIron { get; }
    public IReadOnlyDictionary<string,ConstantDefinition> Constants { get; }
    public IReadOnlyList<WorldCitySpecialDefinition> WorldCitySpecials { get; }
    public IReadOnlyList<HourlyRewardDefinition> HourlyRewards { get; }
    public IReadOnlyDictionary<int, GeneralDefinition> Generals { get; }
    public IReadOnlyDictionary<int, GeneralRecruitDefinition> GeneralRecruits { get; }
    public IReadOnlyDictionary<int, GeneralRecruitDefinition> GeneralRecruitByGeneralId { get; }
    public IReadOnlyList<GeneralPositionDefinition> GeneralPositions { get; }
    public IReadOnlyList<TavernStateTransitionDefinition> TavernStateTransitions { get; }
    public IReadOnlyDictionary<int, ChargeItemDefinition> ChargeItems { get; }
    public IReadOnlyDictionary<int, StringConstantDefinition> StringConstants { get; }
    public IReadOnlyList<EquipSuitDefinition> EquipSuits { get; }
    public IReadOnlyDictionary<int, StoreItemDefinition> StoreItems { get; }
    public IReadOnlyDictionary<int, StoreItemDefinition> StoreItemByEquipmentId { get; }
    public IReadOnlyList<StoreStateTransitionDefinition> StoreStateTransitions { get; }
    public IReadOnlyDictionary<int, EquipmentDefinition> Equipment { get; }
    public IReadOnlyDictionary<int, ItemDefinition> Items { get; }
    public IReadOnlyDictionary<int, TechnologyDefinition> Technologies { get; }
    public IReadOnlyDictionary<int, TroopDefinition> Troops { get; }
    public IReadOnlyDictionary<int, TacticDefinition> Tactics { get; }
    public IReadOnlyDictionary<int, ArmyDefinition> Armies { get; }
    public IReadOnlyDictionary<int, FightStrategyDefinition> FightStrategies { get; }
    public IReadOnlyDictionary<(int attacker,int defender), FightStrategyCoefficientDefinition> FightStrategyCoefficients { get; }
    public IReadOnlyDictionary<int, FightRewardCoefficientDefinition> FightRewardCoefficients { get; }
    public IReadOnlyDictionary<int, TroopConscriptDefinition> TroopConscripts { get; }
    public IReadOnlyDictionary<int, KingdomLevelDefinition> KingdomLevels { get; }
    public IReadOnlyDictionary<int, OfficialDefinition> Officials { get; }
    public IReadOnlyDictionary<int, OfficerAffairDefinition> OfficerAffairs { get; }
    public IReadOnlyDictionary<int, OfficerSpecialtyDefinition> OfficerSpecialties { get; }
    public IReadOnlyDictionary<int, PoliticsEventDefinition> PoliticsEvents { get; }
    public IReadOnlyDictionary<(int building,int degree), HallDefinition> Halls { get; }
    public IReadOnlyDictionary<int,CdExamDefinition> CdExams { get; }
    public IReadOnlyList<CdExamRankingDefinition> CdExamRankings { get; }
    public IReadOnlyList<InvestmentEventDefinition> InvestmentEvents { get; }
    public IReadOnlyDictionary<int,BarbarianDefinition> Barbarians { get; }
    public IReadOnlyList<BarbarianRankingDefinition> BarbarianRankings { get; }
    public IReadOnlyDictionary<int,NationTaskInitDefinition> NationTaskInitial { get; }
    public IReadOnlyDictionary<int,NationTaskTypeDefinition> NationTaskTypes { get; }
    public IReadOnlyList<NationExpansionTargetDefinition> NationExpansionTargets { get; }
    public IReadOnlyList<NationExpansionPeriodDefinition> NationExpansionPeriods { get; }
    public IReadOnlyList<NationBarbarianWaveDefinition> NationBarbarianWaves { get; }
    public IReadOnlyList<NationSweepWaveDefinition> NationSweepWaves { get; }
    public IReadOnlyList<NationBorderWaveDefinition> NationBorderWaves { get; }
    public IReadOnlyList<NationYellowTurbanNpcDefinition> NationYellowTurbanNpcs { get; }
    public IReadOnlyList<NationCompetitionRoadDefinition> NationCompetitionRoads { get; }
    public IReadOnlyDictionary<int,IReadOnlyList<NationTaskRankingRewardDefinition>> NationTaskRankingRewards { get; }
    public IReadOnlyDictionary<int, WorldCityDefinition> WorldCities { get; }
    public IReadOnlyDictionary<int, WorldRoadDefinition> WorldRoads { get; }
    public IReadOnlyDictionary<int,KfgzWorldCityDefinition> KfgzWorldCities { get; }
    public IReadOnlyDictionary<int,KfgzWorldRoadDefinition> KfgzWorldRoads { get; }
    public NameData Names { get; }
    public string BaseDirectory { get; }

    public CanonicalContent(IHostEnvironment env)
    {
        BaseDirectory = ResolveDirectory(env.ContentRootPath, "Data", "Canonical");
        var opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _buildings = Load<BuildingDefinition[]>(BaseDirectory, "buildings.json", opt).ToDictionary(x => x.Id);
        _tasks = Load<TaskDefinition[]>(BaseDirectory, "tasks.json", opt).ToDictionary(x => x.Id);
        MarketProducts=Load<MarketProductDefinition[]>(BaseDirectory,"market_products.json",opt).ToDictionary(x=>x.Id);
        MarketDegrees=Load<MarketDegreeDefinition[]>(BaseDirectory,"market_degree.json",opt);
        MarketIron=Load<MarketIronDefinition[]>(BaseDirectory,"market_iron.json",opt).ToDictionary(x=>x.Id);
        Constants=Load<Dictionary<string,ConstantDefinition>>(BaseDirectory,"constants.json",opt);
        WorldCitySpecials=Load<WorldCitySpecialDefinition[]>(BaseDirectory,"world_city_special.json",opt);
        HourlyRewards=Load<HourlyRewardDefinition[]>(BaseDirectory,"hourly_reward.json",opt);
        var raw = Load<Dictionary<string, Dictionary<string, int>>>(BaseDirectory, "serial.json", opt);
        _serial = raw.ToDictionary(k => int.Parse(k.Key), v => v.Value.ToDictionary(k => int.Parse(k.Key), x => x.Value));
        Names = Load<NameData>(BaseDirectory, "names.json", opt);
        Generals = Load<GeneralDefinition[]>(BaseDirectory, "generals.json", opt).ToDictionary(x=>x.Id);
        GeneralRecruits = Load<GeneralRecruitDefinition[]>(BaseDirectory, "general_recruits.json", opt).ToDictionary(x=>x.Id);
        GeneralRecruitByGeneralId = GeneralRecruits.Values.ToDictionary(x=>x.GeneralId);
        GeneralPositions = Load<GeneralPositionDefinition[]>(BaseDirectory, "general_positions.json", opt);
        TavernStateTransitions = Load<TavernStateTransitionDefinition[]>(BaseDirectory, "tavern_stats.json", opt);
        ChargeItems = Load<ChargeItemDefinition[]>(BaseDirectory, "charge_items.json", opt).ToDictionary(x=>x.Id);
        StringConstants = Load<StringConstantDefinition[]>(BaseDirectory, "string_constants.json", opt).ToDictionary(x=>x.Id);
        EquipSuits = Load<EquipSuitDefinition[]>(BaseDirectory, "equip_suits.json", opt);
        StoreItems = Load<StoreItemDefinition[]>(BaseDirectory, "store_items.json", opt).ToDictionary(x=>x.Id);
        StoreItemByEquipmentId = StoreItems.Values.GroupBy(x=>x.ItemId).ToDictionary(g=>g.Key,g=>g.First());
        StoreStateTransitions = Load<StoreStateTransitionDefinition[]>(BaseDirectory, "store_stats.json", opt);
        Equipment = Load<EquipmentDefinition[]>(BaseDirectory, "equipment.json", opt).ToDictionary(x=>x.Id);
        Items = Load<ItemDefinition[]>(BaseDirectory, "items.json", opt).ToDictionary(x=>x.Id);
        Technologies = Load<TechnologyDefinition[]>(BaseDirectory, "technologies.json", opt).ToDictionary(x=>x.Id);
        Troops = Load<TroopDefinition[]>(BaseDirectory, "troops.json", opt).ToDictionary(x=>x.Id);
        Tactics = Load<TacticDefinition[]>(BaseDirectory, "tactics.json", opt).ToDictionary(x=>x.Id);
        Armies = Load<ArmyDefinition[]>(BaseDirectory, "armies.json", opt).GroupBy(x=>x.GeneralId).ToDictionary(x=>x.Key,x=>x.First());
        FightStrategies = Load<FightStrategyDefinition[]>(BaseDirectory,"fight_strategies.json",opt).ToDictionary(x=>x.Id);
        FightStrategyCoefficients = Load<FightStrategyCoefficientDefinition[]>(BaseDirectory,"fight_strategy_coefficients.json",opt).ToDictionary(x=>(x.AttackerStrategy,x.DefenderStrategy));
        FightRewardCoefficients = Load<FightRewardCoefficientDefinition[]>(BaseDirectory,"fight_reward_coefficients.json",opt).ToDictionary(x=>x.Id);
        TroopConscripts = Load<TroopConscriptDefinition[]>(BaseDirectory,"troop_conscribe.json",opt).ToDictionary(x=>x.TroopId);
        KingdomLevels = Load<KingdomLevelDefinition[]>(BaseDirectory,"kingdom_levels.json",opt).ToDictionary(x=>x.Level);
        Officials = Load<OfficialDefinition[]>(BaseDirectory,"officials.json",opt).ToDictionary(x=>x.Id);
        OfficerAffairs = Load<OfficerAffairDefinition[]>(BaseDirectory,"officer_affairs.json",opt).ToDictionary(x=>x.Id);
        OfficerSpecialties = Load<OfficerSpecialtyDefinition[]>(BaseDirectory,"officer_specialties.json",opt).ToDictionary(x=>x.Id);
        PoliticsEvents = Load<PoliticsEventDefinition[]>(BaseDirectory,"politics_events.json",opt).ToDictionary(x=>x.Id);
        Halls = Load<HallDefinition[]>(BaseDirectory,"halls.json",opt).ToDictionary(x=>(x.Id,x.Degree));
        CdExams = Load<CdExamDefinition[]>(BaseDirectory,"cd_exams.json",opt).ToDictionary(x=>x.Id);
        CdExamRankings = Load<CdExamRankingDefinition[]>(BaseDirectory,"cd_exams_ranking.json",opt);
        InvestmentEvents = Load<InvestmentEventDefinition[]>(BaseDirectory,"kt_tz_ev.json",opt);
        Barbarians = Load<BarbarianDefinition[]>(BaseDirectory,"barbarian.json",opt).ToDictionary(x=>x.Id);
        BarbarianRankings = Load<BarbarianRankingDefinition[]>(BaseDirectory,"barbarian_ranking.json",opt);
        NationTaskInitial = Load<NationTaskInitDefinition[]>(BaseDirectory,"kt_init.json",opt).ToDictionary(x=>x.Id);
        NationTaskTypes = Load<NationTaskTypeDefinition[]>(BaseDirectory,"kt_type.json",opt).ToDictionary(x=>x.Id);
        NationExpansionTargets = Load<NationExpansionTargetDefinition[]>(BaseDirectory,"kt_kj_t.json",opt);
        NationExpansionPeriods = Load<NationExpansionPeriodDefinition[]>(BaseDirectory,"kt_kj_s.json",opt);
        NationBarbarianWaves = Load<NationBarbarianWaveDefinition[]>(BaseDirectory,"kt_mz_s.json",opt);
        NationSweepWaves = Load<NationSweepWaveDefinition[]>(BaseDirectory,"kt_sdmz_s.json",opt);
        NationBorderWaves = Load<NationBorderWaveDefinition[]>(BaseDirectory,"kt_bj_s.json",opt);
        NationYellowTurbanNpcs = Load<NationYellowTurbanNpcDefinition[]>(BaseDirectory,"kt_hj_npc.json",opt);
        NationCompetitionRoads = Load<NationCompetitionRoadDefinition[]>(BaseDirectory,"kindom_task_road.json",opt);
        NationTaskRankingRewards = new Dictionary<int,IReadOnlyList<NationTaskRankingRewardDefinition>>{{1,Load<NationTaskRankingRewardDefinition[]>(BaseDirectory,"kindom_task_ranking.json",opt)},{2,Load<NationTaskRankingRewardDefinition[]>(BaseDirectory,"kt_sd_ranking.json",opt)},{3,Load<NationTaskRankingRewardDefinition[]>(BaseDirectory,"kt_mz_ranking.json",opt)},{4,Load<NationTaskRankingRewardDefinition[]>(BaseDirectory,"kt_tz_ranking.json",opt)},{5,Load<NationTaskRankingRewardDefinition[]>(BaseDirectory,"kt_im_ranking.json",opt)},{6,Load<NationTaskRankingRewardDefinition[]>(BaseDirectory,"kt_kj_ranking.json",opt)},{7,Load<NationTaskRankingRewardDefinition[]>(BaseDirectory,"kt_sdmz_ranking.json",opt)},{8,Load<NationTaskRankingRewardDefinition[]>(BaseDirectory,"kt_bj_ranking.json",opt)},{9,Load<NationTaskRankingRewardDefinition[]>(BaseDirectory,"kt_hj_ranking.json",opt)}};
        WorldCities = Load<WorldCityDefinition[]>(BaseDirectory, "world_cities.json", opt).ToDictionary(x=>x.Id);
        WorldRoads = Load<WorldRoadDefinition[]>(BaseDirectory, "world_roads.json", opt).ToDictionary(x=>x.Id);
        KfgzWorldCities=Load<KfgzWorldCityDefinition[]>(BaseDirectory,"kfgz_world_city.json",opt).ToDictionary(x=>x.CityId);
        KfgzWorldRoads=Load<KfgzWorldRoadDefinition[]>(BaseDirectory,"kfgz_world_road.json",opt).ToDictionary(x=>x.RoadId);
    }

    static string ResolveDirectory(string contentRoot, params string[] parts)
    {
        var relative = Path.Combine(parts);
        var candidates = new[]
        {
            Path.Combine(contentRoot, relative),
            Path.Combine(AppContext.BaseDirectory, relative),
            Path.GetFullPath(Path.Combine(contentRoot, "..", "..", relative)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relative))
        };
        return candidates.FirstOrDefault(Directory.Exists)
               ?? throw new DirectoryNotFoundException($"Canonical data directory not found. Tried: {string.Join(" | ", candidates)}");
    }

    static T Load<T>(string dir, string file, JsonSerializerOptions opt) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(dir, file)), opt)
        ?? throw new InvalidOperationException($"Cannot load {file}");

    public int GeneralPositionCount(int playerLevel, int type) =>
        GeneralPositions.Count(x => x.Type == type && x.OpenLevel <= playerLevel);

    public IReadOnlyList<TavernStateTransitionDefinition> TavernTransitionsFrom(int preState) =>
        TavernStateTransitions.Where(x => x.PreState == preState).OrderBy(x => x.NextState).ToArray();


    public int MaxStoreQuality(int playerLevel, int storeType)
    {
        var q = EquipSuits.Where(x => x.Type == storeType && x.MinChiefLevel <= playerLevel).Select(x => x.Quality);
        return q.Any() ? Math.Max(1, q.Max()) : 1;
    }

    public IReadOnlyList<EquipmentDefinition> EquipmentAvailableForStoreType(int playerLevel, int storeType)
    {
        var ids = EquipSuits.Where(x => x.Type == storeType && x.MinChiefLevel <= playerLevel)
            .SelectMany(x => x.EquipmentIds).Distinct().ToHashSet();
        return Equipment.Values.Where(x => ids.Contains(x.Id)).ToArray();
    }

    public IReadOnlyList<StoreStateTransitionDefinition> StoreTransitionsFrom(int preState) =>
        StoreStateTransitions.Where(x => x.PreState == preState).OrderBy(x => x.NextState).ToArray();

    // Legacy SerialCache.getIntiLv(points): serial id=3 stores cumulative intimacy thresholds.
    public int IntimacyLevel(int points)
    {
        if (!_serial.TryGetValue(3, out var levels) || levels.Count == 0) return 1;
        foreach (var pair in levels.OrderBy(x => x.Key))
            if (points < pair.Value) return pair.Key;
        return levels.Keys.Max();
    }

    // Legacy EquipSuitCache.getNowMaxIntimacyLv(playerLv) only builds its map from military (type=1) suits.
    public int MaxIntimacyLevelForPlayer(int playerLevel)
    {
        var eligible = EquipSuits.Where(x => x.Type == 1 && x.MinChiefLevel <= playerLevel)
            .OrderBy(x => x.MinChiefLevel).ToArray();
        return eligible.Length == 0 ? 1 : Math.Max(1, eligible[^1].MaxIntimacyLevel);
    }

    public int Serial(int seriesId, int index)
    {
        if (!_serial.TryGetValue(seriesId, out var s) || !s.TryGetValue(index, out var v))
            throw new KeyNotFoundException($"Legacy serial missing series={seriesId}, index={index}");
        return v;
    }
}

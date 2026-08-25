namespace CTXD.Server.Models;

public sealed record RegisterRequest(string Username, string Password);
public sealed record LoginRequest(string Username, string Password);
public sealed record AuthResponse(string Token, PlayerView Player);
public sealed record ForceRequest(int ForceId);
public sealed record SetNameRequest(string Name, int Pic);

public sealed record PlayerView(long Id, string? Name, int Pic, int ForceId, int Level, long Exp,
    int CurrentTaskId, bool CanChooseName, int ConstructionSlots, int FreeConstructionNum,
    long SysGold, long UserGold, int ConsumeLevel, IReadOnlyList<int> FunctionIds);
public sealed record ResourceView(long Copper, long Wood, long Food, long Iron, DateTimeOffset UpdateTime,
    int CopperPerHour, int WoodPerHour, int FoodPerHour, int IronPerHour,
    long CopperMax, long WoodMax, long FoodMax, long IronMax);
public sealed record BuildingView(int Id, string Name, int Level, int State, DateTimeOffset? CompleteAt,
    int OutputType, int OutputPerHour, int NextCopperCost, int NextWoodCost, int NextDurationMs);
public sealed record MainCityResponse(PlayerView Player, ResourceView Resources, IReadOnlyList<BuildingView> Buildings);
public sealed record UpgradeResponse(BuildingView Building, ResourceView Resources);
public sealed record RandomNamesResponse(IReadOnlyList<string> List);
public sealed record ApiError(string Code, string Message);

public sealed record GeneralView(int Id, string Name, int Type, string Pic, int Quality, int Level, long Exp,
    int Leader, int Strength, int Intel, int Politics, int TroopId, int TacticId, int StratagemId,
    int Forces, int LocationId, int State, int Morale, int AutoState);
public sealed record GeneralRosterResponse(int CivilMax, int MilitaryMax, IReadOnlyList<GeneralView> Civil, IReadOnlyList<GeneralView> Military);
public sealed record TavernOfferView(int Position, int GeneralId, string Name, string Pic, int Quality, int Type,
    bool Locked, bool Bought, bool IsGold, int Price, int Leader, int Strength, int Intel, int Politics,
    int TroopId, int TacticId, int StratagemId);
public sealed record TavernResponse(int Type, int State, int RefreshCount, DateTimeOffset NextRefreshAt,
    int NowGeneralNum, int MaxGeneralNum, IReadOnlyList<TavernOfferView> Offers);
public sealed record RecruitGeneralResponse(GeneralView General, ResourceView Resources, int NowGeneralNum, int MaxGeneralNum);

public sealed record StoreOfferView(
    int Position, int EquipmentId, string Name, string Pic, int Quality, int GoodsType, int Level,
    bool Locked, bool Bought, bool IsGold, bool IsCheap, int Price, int Attribute, string RefreshAttribute);
public sealed record StoreResponse(
    int Type, int State, int RefreshCount, DateTimeOffset NextRefreshAt, int Intimacy,
    int NowItemNum, int MaxItemNum, int CurMaxQuality, IReadOnlyList<StoreOfferView> Offers);
public sealed record PlayerEquipmentView(
    long InstanceId, int EquipmentId, string Name, string Pic, int GoodsType, int Quality, int Level,
    int Attribute, int? OwnerGeneralId, string RefreshAttribute, int GemId, int QuenchingTimes,
    int State, int Num, int CopperSold);
public sealed record InventoryResponse(int NowItemNum, int MaxItemNum, IReadOnlyList<PlayerEquipmentView> Items);
public sealed record BuyEquipmentResponse(PlayerEquipmentView Item, ResourceView Resources, int NowItemNum, int MaxItemNum);
public sealed record EquipRequest(int GeneralId);
public sealed record EquipEquipmentResponse(PlayerEquipmentView Item, PlayerEquipmentView? Replaced);
public sealed record SellEquipmentResponse(long CopperGained, ResourceView Resources, int NowItemNum, int MaxItemNum);

public sealed record TechnologyResourceCost(string Type, long Value);
public sealed record TechnologyView(
    int Id, int Key, string KeyString, string Name, string Pic, string Intro,
    int Status, int InjectedCount, int RequiredInjections,
    DateTimeOffset? ResearchCompleteAt, int ResearchDurationMs,
    bool IsNew, bool FinishNew, IReadOnlyList<TechnologyResourceCost> Resources,
    double[] Parameters);
public sealed record TechnologyListResponse(int CurrentPage, int TotalPage, IReadOnlyList<TechnologyView> Technologies);
public sealed record TechnologyInjectResponse(TechnologyView Technology, ResourceView Resources);
public sealed record TechnologyResearchResponse(TechnologyView Technology);

public sealed record WorldCityView(
    int Id, string Name, int Type, int Terrain, int TerrainEffectType, int Output, int Chief,
    int[] Npcs, int WeiDistance, int ShuDistance, int WuDistance,
    int WeiArea, int ShuArea, int WuArea, int WeiMask, int ShuMask, int WuMask, int ShowMask,
    string Pic, string Intro, int X, int Y, string Model,
    int OwnerForceId, int State, int Title, int Border,
    bool Discovered, bool Attackable, bool Fogged);
public sealed record WorldRoadView(int Id, int Start, int End, int Length, string Trace);
public sealed record WorldMoveView(
    int GeneralId, int RoadId, int FromCityId, int ToCityId,
    DateTimeOffset StartedAt, DateTimeOffset ArrivesAt, int[] PathCityIds, int PathIndex);
public sealed record WorldBattleHandoffView(
    long Id, int CityId, long AttackerPlayerId, int AttackerGeneralId,
    int AttackerForceId, int DefenderForceId, int BattleType, int Status,
    int? WinnerForceId, DateTimeOffset CreatedAt, DateTimeOffset? ResolvedAt);
public sealed record WorldResponse(
    int CapitalCityId, int? FocusGeneralId, IReadOnlyList<WorldCityView> Cities,
    IReadOnlyList<WorldRoadView> Roads, IReadOnlyList<WorldMoveView> Moves,
    IReadOnlyList<WorldBattleHandoffView> Battles);
public sealed record WorldMoveRequest(int CityId);
public sealed record WorldEventBattleRequest(int GeneralId,long EventNpcId);
public sealed record WorldScheduledEventNpcView(long Id,int TaskType,int CityId,int ArmyId,DateTimeOffset SpawnedAt);
public sealed record WorldBattleResultRequest(bool AttackerWon, string? ResultPayload);
public sealed record WorldBattleResultResponse(long BattleId, int CityId, int WinnerForceId, bool Conquered);
public sealed record BattleJoinRequest(int GeneralId);
public sealed record BattleActionRequest(int GeneralId, int ActionType, int StrategyId);
public sealed record WorldCityDetailResponse(
    WorldCityView City, bool InBattle, WorldBattleHandoffView? Battle,
    IReadOnlyList<int> NeighborCityIds, IReadOnlyList<GeneralView> PlayerGenerals);

public sealed record BattleUnitView(long Id, int Side, int Sequence, long? PlayerId, int GeneralId, int TroopId,
    string Name, int Level, int Attack, int Defense, int Leader, int Strength, int Hp, int MaxHp, bool IsNpc, bool Dead,
    int TacticId, bool TacticAvailable, int StrategyId, int[] AllowedStrategyIds, int SelectedAction);
public sealed record BattleRoundView(int RoundNo, long AttackerUnitId, long DefenderUnitId, int AttackerDamage,
    int DefenderDamage, int AttackerHp, int DefenderHp, int WinnerSide, int[] AttackerTicks, int[] DefenderTicks);
public sealed record BattleView(long Id, int CityId, int Status, int RoundNo, int? WinnerSide,
    IReadOnlyList<BattleUnitView> Attackers, IReadOnlyList<BattleUnitView> Defenders, IReadOnlyList<BattleRoundView> Rounds);
public sealed record NationForceView(int ForceId,int Level,long Exp,int MaxExp);
public sealed record NationView(int PlayerForceId,int ForceLevel,long ForceExp,int MaxExp,int OfficialId,string OfficialName,bool SalaryAvailable,int Salary,IReadOnlyList<NationForceView> Nations);
public sealed record NationSalaryResponse(int Output,bool SalaryAvailable);
public sealed record CivilAffairView(int AffairId,string Name,int Level,int OutputType,int UnitOutput,int IntervalMinutes,DateTimeOffset? StartedAt);
public sealed record CivilAffairsView(int GeneralId,IReadOnlyList<CivilAffairView> Affairs);
public sealed record CivilAffairRequest(int GeneralId,int AffairId);
public sealed record CivilAffairReward(int Type,int Count);
public sealed record PoliticsEventView(int BuildingId,int EventId,string Name,string Description,string Picture,string Option1,string Option2,string Reward1,string Reward2,int Gold1,int Gold2);
public sealed record PoliticsView(int EventCount,int PeopleLoyal,IReadOnlyList<PoliticsEventView> Events);
public sealed record PoliticsChoiceRequest(int BuildingId,int Option);
public sealed record PoliticsReward(string Type,int Value);
public sealed record OfficeMemberView(long PlayerId,string Name,int Level,bool IsLeader,int State);
public sealed record OfficeView(int BuildingId,string LeaderTitle,string MemberTitle,long OwnerPlayerId,bool AutoPass,IReadOnlyList<OfficeMemberView> Members);
public sealed record OfficeApplyRequest(int BuildingId);
public sealed record OfficeMemberRequest(long PlayerId);
public sealed record OfficeAutoPassRequest(bool Enabled);
public sealed record PositionBattleRequest(int BuildingId,int GeneralId);
public sealed record NationTrialStageView(int Stage,int RequiredKills);
public sealed record NationTrialView(int ForceId,int TrialId,int Stage,DateTimeOffset? EndsAt,bool Won,int PlayerKills,int Rank,int CityId,string Name,IReadOnlyList<NationTrialStageView> Stages,bool RewardAvailable);
public sealed record NationTrialReward(int WinExp,int WinIron,int RankExp,int RankIron);
public sealed record NationRankEntry(int Rank,long PlayerId,string Name,long Value);
public sealed record NationTaskView(int Type,long Target,long Progress,DateTimeOffset? EndsAt,bool Won,long PlayerScore,DateTimeOffset? InvestAvailableAt);
public sealed record NationInvestmentReward(int Copper,int CopperExtra,int Exp,int ExpExtra,DateTimeOffset AvailableAt);
public sealed record NationProtectionView(int ForceId,int AttackingForceId,int CityId,int TrialId,DateTimeOffset EndsAt,bool? Won,int PlayerKills,int Rank,bool RewardAvailable);
public sealed record NationTrialBattleRequest(int CityId,int GeneralId);
public sealed record NationScheduledTaskView(long Id,int TaskType,int TaskId,long Target,long Progress,DateTimeOffset StartsAt,DateTimeOffset EndsAt,int Status,string DependencyCode,int EventSerial);

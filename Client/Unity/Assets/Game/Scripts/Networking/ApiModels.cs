using System;

namespace CTXD.Client.Networking
{
    [Serializable] public class RegisterRequest { public string username; public string password; }
    [Serializable] public class LoginRequest { public string username; public string password; }
    [Serializable] public class ForceRequest { public int forceId; }
    [Serializable] public class SetNameRequest { public string name; public int pic; }

    [Serializable] public class AuthResponse { public string token; public PlayerView player; }
    [Serializable] public class PlayerView
    {
        public long id;
        public string name;
        public int pic;
        public int forceId;
        public int level;
        public long exp;
        public int currentTaskId;
        public bool canChooseName;
        public int constructionSlots;
        public int freeConstructionNum;
        public long sysGold;
        public long userGold;
        public int consumeLevel;
        public int[] functionIds;
    }
    [Serializable] public class ResourceView
    {
        public long copper;
        public long wood;
        public long food;
        public long iron;
        public string updateTime;
        public int copperPerHour;
        public int woodPerHour;
        public int foodPerHour;
        public int ironPerHour;
        public long copperMax;
        public long woodMax;
        public long foodMax;
        public long ironMax;
    }
    [Serializable] public class BuildingView
    {
        public int id;
        public string name;
        public int level;
        public int state;
        public string completeAt;
        public int outputType;
        public int outputPerHour;
        public int nextCopperCost;
        public int nextWoodCost;
        public int nextDurationMs;
    }
    [Serializable] public class BuildingArray { public BuildingView[] items; }
    [Serializable] public class MainCityResponse { public PlayerView player; public ResourceView resources; public BuildingView[] buildings; }
    [Serializable] public class UpgradeResponse { public BuildingView building; public ResourceView resources; }
    [Serializable] public class RandomNamesResponse { public string[] list; }
    [Serializable] public class ApiError { public string code; public string message; }

    [Serializable] public class GeneralView
    {
        public int id; public string name; public int type; public string pic; public int quality;
        public int level; public long exp; public int leader; public int strength; public int intel; public int politics;
        public int troopId; public int tacticId; public int stratagemId; public int forces; public int locationId;
        public int state; public int morale; public int autoState;
    }
    [Serializable] public class GeneralRosterResponse
    {
        public int civilMax; public int militaryMax; public GeneralView[] civil; public GeneralView[] military;
    }
    [Serializable] public class TavernOfferView
    {
        public int position; public int generalId; public string name; public string pic; public int quality; public int type;
        public bool locked; public bool bought; public bool isGold; public int price; public int leader; public int strength;
        public int intel; public int politics; public int troopId; public int tacticId; public int stratagemId;
    }
    [Serializable] public class TavernResponse
    {
        public int type; public int state; public int refreshCount; public string nextRefreshAt; public int nowGeneralNum;
        public int maxGeneralNum; public TavernOfferView[] offers;
    }
    [Serializable] public class RecruitGeneralResponse
    {
        public GeneralView general; public ResourceView resources; public int nowGeneralNum; public int maxGeneralNum;
    }

    [Serializable] public class StoreOfferView
    {
        public int position; public int equipmentId; public string name; public string pic; public int quality; public int goodsType; public int level;
        public bool locked; public bool bought; public bool isGold; public bool isCheap; public int price; public int attribute; public string refreshAttribute;
    }
    [Serializable] public class StoreResponse
    {
        public int type; public int state; public int refreshCount; public string nextRefreshAt; public int intimacy;
        public int nowItemNum; public int maxItemNum; public int curMaxQuality; public StoreOfferView[] offers;
    }
    [Serializable] public class PlayerEquipmentView
    {
        public long instanceId; public int equipmentId; public string name; public string pic; public int goodsType; public int quality; public int level;
        public int attribute; public int ownerGeneralId; public string refreshAttribute; public int gemId; public int quenchingTimes;
        public int state; public int num; public int copperSold;
    }
    [Serializable] public class InventoryResponse
    {
        public int nowItemNum; public int maxItemNum; public PlayerEquipmentView[] items;
    }
    [Serializable] public class BuyEquipmentResponse
    {
        public PlayerEquipmentView item; public ResourceView resources; public int nowItemNum; public int maxItemNum;
    }
    [Serializable] public class EquipRequest { public int generalId; }
    [Serializable] public class EquipEquipmentResponse { public PlayerEquipmentView item; public PlayerEquipmentView replaced; }
    [Serializable] public class SellEquipmentResponse
    {
        public long copperGained; public ResourceView resources; public int nowItemNum; public int maxItemNum;
    }
    [Serializable] public class TechnologyResourceCost { public string type; public long value; }
    [Serializable] public class TechnologyView
    {
        public int id; public int key; public string keyString; public string name; public string pic; public string intro;
        public int status; public int injectedCount; public int requiredInjections; public string researchCompleteAt; public int researchDurationMs;
        public bool isNew; public bool finishNew; public TechnologyResourceCost[] resources; public double[] parameters;
    }
    [Serializable] public class TechnologyListResponse
    {
        public int currentPage; public int totalPage; public TechnologyView[] technologies;
    }
    [Serializable] public class TechnologyInjectResponse { public TechnologyView technology; public ResourceView resources; }
    [Serializable] public class TechnologyResearchResponse { public TechnologyView technology; }

    [Serializable] public class WorldCityView
    {
        public int id; public string name; public int type; public int terrain; public int terrainEffectType;
        public int output; public int chief; public int[] npcs; public int weiDistance; public int shuDistance; public int wuDistance;
        public int weiArea; public int shuArea; public int wuArea; public int weiMask; public int shuMask; public int wuMask;
        public int showMask; public string pic; public string intro; public int x; public int y; public string model;
        public int ownerForceId; public int state; public int title; public int border;
        public bool discovered; public bool attackable; public bool fogged;
    }
    [Serializable] public class WorldRoadView { public int id; public int start; public int end; public int length; public string trace; }
    [Serializable] public class WorldMoveView
    {
        public int generalId; public int roadId; public int fromCityId; public int toCityId;
        public string startedAt; public string arrivesAt; public int[] pathCityIds; public int pathIndex;
    }
    [Serializable] public class WorldBattleHandoffView
    {
        public long id; public int cityId; public long attackerPlayerId; public int attackerGeneralId;
        public int attackerForceId; public int defenderForceId; public int battleType; public int status;
        public int winnerForceId; public string createdAt; public string resolvedAt;
    }
    [Serializable] public class WorldResponse
    {
        public int capitalCityId; public int focusGeneralId; public WorldCityView[] cities;
        public WorldRoadView[] roads; public WorldMoveView[] moves; public WorldBattleHandoffView[] battles;
    }
    [Serializable] public class WorldCityDetailResponse
    {
        public WorldCityView city; public bool inBattle; public WorldBattleHandoffView battle;
        public int[] neighborCityIds; public GeneralView[] stationedGenerals;
    }
    [Serializable] public class WorldMoveRequest { public int cityId; }

    [Serializable] public class BattleUnitView
    {
        public long id; public int side; public int sequence; public long playerId; public int generalId; public int troopId;
        public string name; public int level; public int attack; public int defense; public int leader; public int strength;
        public int hp; public int maxHp; public bool isNpc; public bool dead;
        public int tacticId; public bool tacticAvailable; public int strategyId; public int[] allowedStrategyIds; public int selectedAction;
    }
    [Serializable] public class BattleActionRequest { public int generalId; public int actionType; public int strategyId; }
    [Serializable] public class BattleRoundView
    {
        public int roundNo; public long attackerUnitId; public long defenderUnitId; public int attackerDamage; public int defenderDamage;
        public int attackerHp; public int defenderHp; public int winnerSide; public int[] attackerTicks; public int[] defenderTicks;
    }
    [Serializable] public class BattleView
    {
        public long id; public int cityId; public int status; public int roundNo; public int winnerSide;
        public BattleUnitView[] attackers; public BattleUnitView[] defenders; public BattleRoundView[] rounds;
    }
    [Serializable] public class NationForceView { public int forceId; public int level; public long exp; public int maxExp; }
    [Serializable] public class NationView { public int playerForceId; public int forceLevel; public long forceExp; public int maxExp; public int officialId; public string officialName; public bool salaryAvailable; public int salary; public NationForceView[] nations; }
    [Serializable] public class NationSalaryResponse { public int output; public bool salaryAvailable; }
    [Serializable] public class PoliticsEventView { public int buildingId; public int eventId; public string name; public string description; public string picture; public string option1; public string option2; public string reward1; public string reward2; public int gold1; public int gold2; }
    [Serializable] public class PoliticsView { public int eventCount; public int peopleLoyal; public PoliticsEventView[] events; }
    [Serializable] public class PoliticsChoiceRequest { public int buildingId; public int option; }
    [Serializable] public class PoliticsReward { public string type; public int value; }
    [Serializable] public class CivilAffairView { public int affairId; public string name; public int level; public int outputType; public int unitOutput; public int intervalMinutes; public string startedAt; }
    [Serializable] public class CivilAffairsView { public int generalId; public CivilAffairView[] affairs; }
    [Serializable] public class CivilAffairRequest { public int generalId; public int affairId; }
    [Serializable] public class OfficeMemberView { public long playerId; public string name; public int level; public bool isLeader; public int state; }
    [Serializable] public class OfficeView { public int buildingId; public string leaderTitle; public string memberTitle; public long ownerPlayerId; public bool autoPass; public OfficeMemberView[] members; }
    [Serializable] public class OfficeApplyRequest { public int buildingId; }
    [Serializable] public class OfficeMemberRequest { public long playerId; }
    [Serializable] public class OfficeAutoPassRequest { public bool enabled; }
    [Serializable] public class PositionBattleRequest { public int buildingId; public int generalId; }
    [Serializable] public class PositionBattleResponse { public long battleId; }
    [Serializable] public class NationTrialStageView { public int stage; public int requiredKills; }
    [Serializable] public class NationTrialView { public int forceId; public int trialId; public int stage; public string endsAt; public bool won; public int playerKills; public int rank; public int cityId; public string name; public NationTrialStageView[] stages; public bool rewardAvailable; }
    [Serializable] public class NationTrialReward { public int winExp; public int winIron; public int rankExp; public int rankIron; }
    [Serializable] public class NationRankEntry { public int rank; public long playerId; public string name; public long value; }
    [Serializable] public class NationTaskView { public int type; public long target; public long progress; public string endsAt; public bool won; public long playerScore; public string investAvailableAt; }
    [Serializable] public class NationInvestmentReward { public int copper; public int copperExtra; public int exp; public int expExtra; public string availableAt; }
    [Serializable] public class NationProtectionView { public int forceId; public int attackingForceId; public int cityId; public int trialId; public string endsAt; public bool won; public int playerKills; public int rank; public bool rewardAvailable; }
    [Serializable] public class NationTrialBattleRequest { public int cityId; public int generalId; }
    [Serializable] public class MailAttachmentView { public string kind; public int amount; public int itemId; public int itemType; }
    [Serializable] public class MailView { public long id; public string sender; public string title; public string body; public int type; public bool isRead; public bool isDeleted; public bool isSaved; public MailAttachmentView[] attachments; public bool attachmentsClaimed; public string createdAt; }
    [Serializable] public class MailPage { public MailView[] items; public int page; public int totalPages; public int unread; }
    [Serializable] public class MailClaimResponse { public MailAttachmentView[] items; }
    [Serializable] public class MarketOfferView { public int slot; public int productId; public string itemType; public int itemNum; public string costType; public int costNum; public int quality; }
    [Serializable] public class MarketView { public MarketOfferView[] offers; public int canBuy; public string refreshAt; }
    [Serializable] public class MarketBuyRequest { public int slot; public string requestKey; }
    [Serializable] public class MarketBuyResult { public string itemType; public int added; public int remaining; }
    [Serializable] public class BlackMarketView { public int spend; public int receive; public string cooldownUntil; public int cooldownMaxMinutes; }
    [Serializable] public class BlackMarketTradeRequest { public int left; public int right; public string requestKey; }
    [Serializable] public class BlackMarketTradeResult { public int type; public int received; public string cooldownUntil; }
    [Serializable] public class ChatSendRequest { public string type; public string to; public string message; }
    [Serializable] public class ChatMessageView { public long id; public string type; public long fromId; public string from; public string to; public string message; public string createdAt; }
    [Serializable] public class ChatHistoryResponse { public ChatMessageView[] items; }
    [Serializable] public class BlacklistView { public long id; public long playerId; public string name; }
    [Serializable] public class BlacklistResponse { public BlacklistView[] items; }
    [Serializable] public class OnlineGiftView { public int remaining; public int available; public string nextAt; }
    [Serializable] public class OnlineGiftClaimRequest { public string requestKey; }
    [Serializable] public class OnlineGiftReward { public int rewardId; public int food; public int remaining; public int available; }
    [Serializable] public class DailyGiftView { public bool available; public string resetsAt; }
    [Serializable] public class DailyGiftRequest { public string requestKey; }
    [Serializable] public class DailyGiftReward { public int comboId; public int[] cards; public int gold; public int worship; }
    [Serializable] public class BattleExpActivityView { public long activityId; public bool active; public bool activated; public string condition; public int addPercent; public string endsAt; }
    [Serializable] public class LevelExpActivityView { public long activityId; public bool active; public bool rewardAvailable; public double startLevel; public double currentLevel; public double targetLevel; public int rewardExp; public string endsAt; }
    [Serializable] public class LevelExpActivityReward { public int addedExp; }
    [Serializable] public class DragonActivityView { public long activityId; public bool active; public int score; public int occupy; public int assist; public int cheer; public int dragonNum; public int[] thresholds; public int[] boxRewards; public string endsAt; }
    [Serializable] public class DragonUseRequest { public string requestKey; }
    [Serializable] public class DragonReward { public int type; public int num; public int quality; public int critical; }
    [Serializable] public class IronTier { public long iron; public int rewardIron; }
    [Serializable] public class IronActivityView { public long activityId; public long iron; public int rewardTimes; public int received; public int needIron; public long remainingIron; public string endsAt; public IronTier[] tiers; }
    [Serializable] public class IronClaimRequest { public string requestKey; }
    [Serializable] public class IronClaimResult { public int iron; public int received; }
    [Serializable] public class DstqTier { public int gold; public int reward; public int itemId; }
    [Serializable] public class DstqActivityView { public long activityId; public int gold; public int level; public int needGold; public int ticket106; public int ticket107; public int remaining106; public int remaining107; public string endsAt; public DstqTier[] tiers; }
    [Serializable] public class VipBenefitView { public int vip; public int sequence; public string kind; public int amount; public bool claimed; public bool available; }
    [Serializable] public class VipView { public int vipLevel; public bool teamTimesClaimed; public int teamTimes; public int teamTimesReward; public VipBenefitView[] benefits; }
    [Serializable] public class VipClaimResult { public int vip; public int sequence; public string kind; public int amount; }
    [Serializable] public class TeamMemberView { public long playerId; public string name; public int generalId; public string generalName; public int level; public int forces; }
    [Serializable] public class TeamView { public string id; public string name; public long ownerPlayerId; public int teamType; public int maxGenerals; public string expiresAt; public TeamMemberView[] members; public bool isOwner; public bool inspired; public float inspireEffect; public bool ordered; }
    [Serializable] public class TeamListView { public TeamView[] items; }
    [Serializable] public class TeamCreateRequest { public int teamType=1; }
    [Serializable] public class TeamJoinRequest { public string teamId; public int[] generalIds; }
    [Serializable] public class TeamCostView { public string teamId; public int curNum; public int maxNum; public long totalForces; public bool free; public int deployGold; public bool ordered; public int inspireCost; public float inspireEffect; public int inspireExp; }
    [Serializable] public class TeamDeployRequest { public string teamId; public long battleId; public int curNum; public int teamBattleType; }
    [Serializable] public class TeamDeployResult { public long battleId; public int deployed; public BattleView battle; }
    [Serializable] public class KfwdSignupRequest { public int[] generalIds; }
    [Serializable] public class KfwdMatchView { public long id; public int round; public long opponentPlayerId; public long battleId; public int state; public long winnerPlayerId; public string startsAt; public string deadlineAt; }
    [Serializable] public class KfwdView { public long seasonId; public int seasonNo; public int globalState; public string nextStateAt; public int minLevel; public bool eligible; public bool signed; public bool synced; public int scheduleId; public long competitorId; public int wins; public int losses; public int tickets; public KfwdMatchView match; }
    [Serializable] public class KfwdRankEntry { public int rank; public long playerId; public string name; public long competitorId; public int score; public int wins; public long winRes; public int tickets; }
    [Serializable] public class KfwdRanking { public KfwdRankEntry[] items; }
    [Serializable] public class KfzbSyncRequest { public int[] generalIds; }
    [Serializable] public class KfzbMatchView { public long id; public int phase; public int layer; public int round; public int legacyMatchId; public long opponentPlayerId; public long battleId; public int state; public long winnerPlayerId; public string startsAt; public string deadlineAt; }
    [Serializable] public class KfzbView { public long seasonId; public int seasonNo; public int globalState; public string nextStateAt; public int minLevel; public int supportLevel; public bool eligible; public bool signed; public bool synced; public bool eliminated; public long competitorId; public int wins; public int losses; public KfzbMatchView match; }
    [Serializable] public class KfzbTableEntry { public long matchId; public int phase; public int layer; public int round; public int legacyMatchId; public long player1Id; public string player1; public long player2Id; public string player2; public int state; public long winnerPlayerId; public long battleId; }
    [Serializable] public class KfzbTable { public KfzbTableEntry[] items; }
    [Serializable] public class KfzbSupportRequest { public long matchId; public long competitorId; }
    [Serializable] public class KfzbSupportResult { public long matchId; public long competitorId; public int potentialTicket; public int flower1; public int flower2; }
    [Serializable] public class KfzbSupportClaimResult { public int cards; public int freeCards; }
    [Serializable] public class KfzbFeastCardView { public int freeCards; public int goldCards; public int cardsBought; public int goldCard1; public int goldCard10; public int drinkNum; public int goldDrink; }
    [Serializable] public class KfzbFeastCardBuyRequest { public int type; }
    [Serializable] public class KfzbFeastCardBuyResult { public int cards; public int goldSpent; public int freeCards; public int goldCards; public int cardsBought; }
    [Serializable] public class KfzbFeastJoinRequest { public int rank; public int cardType; }
    [Serializable] public class KfzbFeastParticipantView { public long playerId; public string name; public int forceId; public int titleId; public int tickets; }
    [Serializable] public class KfzbFeastRoomView { public long roomId; public int rank; public int state; public bool drink; public string expiresAt; public KfzbFeastParticipantView[] participants; }
    [Serializable] public class KfzbFeastDrinkResult { public int goldSpent; public int drinkNum; }
    [Serializable] public class KfgzResourceView { public long gold; public long copper; public long wood; public long food; public long iron; }
    [Serializable] public class KfgzGeneralStateView { public int generalId; public int level; public long forces; public int state; public int cityId; public long battleId; }
    [Serializable] public class KfgzView { public long seasonId; public int seasonNo; public int state; public string nextStateAt; public bool signed; public long competitorId; public int forceId; public int[] generalIds; public long version; public KfgzResourceView resources; public KfgzGeneralStateView[] generals; }
    [Serializable] public class KfgzMoveRequest { public int generalId; public int cityId; }
    [Serializable] public class KfgzRetreatRequest { public int[] generalIds; public int cityId; }
    [Serializable] public class KfgzCityView { public int id; public string name; public int type; public int terrain; public int ownerSide; public int ownerForce; public int state; }
    [Serializable] public class KfgzRoadView { public int id; public int start; public int end; public int length; public bool connected; public int nextChangeSeconds; }
    [Serializable] public class KfgzDeploymentView { public long playerId; public int generalId; public int cityId; public int state; public long battleId; }
    [Serializable] public class KfgzBattleView { public long id; public long battleId; public int cityId; public long attackerPlayerId; public long defenderPlayerId; public int state; public long winnerPlayerId; }
    [Serializable] public class KfgzWarView { public long roundId; public int round; public int worldId; public int state; public string startsAt; public string deadlineAt; public int side; public int force1; public int force2; public int winnerSide; public int side1Cities; public int side2Cities; public KfgzCityView[] cities; public KfgzRoadView[] roads; public KfgzDeploymentView[] deployments; public KfgzBattleView[] battles; }
    [Serializable] public class KfgzRankEntry { public int rank; public long playerId; public string name; public int forceId; public long competitorId; public long killArmy; public int occupyCity; public int soloWins; public int wins; public int losses; }
    [Serializable] public class KfgzRanking { public KfgzRankEntry[] items; }

}

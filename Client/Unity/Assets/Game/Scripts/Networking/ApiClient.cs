using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CTXD.Client.Networking
{
    public sealed class ApiException : Exception
    {
        public readonly string Code;
        public ApiException(string code, string message) : base(message) { Code = code; }
    }

    public sealed class ApiClient
    {
        public string BaseUrl { get; set; } = "http://127.0.0.1:5080";
        public string Token { get; set; }

        public Task<AuthResponse> RegisterAsync(string username, string password) =>
            SendAsync<AuthResponse>("POST", "/api/auth/register", new RegisterRequest { username = username, password = password }, false);

        public Task<AuthResponse> LoginAsync(string username, string password) =>
            SendAsync<AuthResponse>("POST", "/api/auth/login", new LoginRequest { username = username, password = password }, false);

        public Task<PlayerView> GetPlayerAsync() => SendAsync<PlayerView>("GET", "/api/player", null, true);
        public Task<KfwdView> GetKfwdAsync() => SendAsync<KfwdView>("GET","/api/kfwd",null,true);
        public Task<KfwdRanking> GetKfwdRankingAsync() => SendAsync<KfwdRanking>("GET","/api/kfwd/ranking",null,true);
        public Task<KfwdView> SignupKfwdAsync(int[] generalIds) => SendAsync<KfwdView>("POST","/api/kfwd/signup",new KfwdSignupRequest{generalIds=generalIds},true);
        public Task<KfwdView> SyncKfwdAsync(int[] generalIds) => SendAsync<KfwdView>("POST","/api/kfwd/sync",new KfwdSignupRequest{generalIds=generalIds},true);
        public Task<KfzbView> GetKfzbAsync() => SendAsync<KfzbView>("GET","/api/kfzb",null,true);
        public Task<KfzbTable> GetKfzbTableAsync() => SendAsync<KfzbTable>("GET","/api/kfzb/table",null,true);
        public Task<KfzbView> SignupKfzbAsync() => SendAsync<KfzbView>("POST","/api/kfzb/signup",null,true);
        public Task<KfzbView> SyncKfzbAsync(int[] generalIds) => SendAsync<KfzbView>("POST","/api/kfzb/sync",new KfzbSyncRequest{generalIds=generalIds},true);
        public Task<KfzbSupportResult> SupportKfzbAsync(long matchId,long competitorId) => SendAsync<KfzbSupportResult>("POST","/api/kfzb/support",new KfzbSupportRequest{matchId=matchId,competitorId=competitorId},true);
        public Task<KfzbSupportClaimResult> ClaimKfzbSupportAsync() => SendAsync<KfzbSupportClaimResult>("POST","/api/kfzb/support/claim",null,true);
        public Task<KfzbFeastCardView> GetKfzbFeastCardsAsync() => SendAsync<KfzbFeastCardView>("GET","/api/kfzb/feast/cards",null,true);
        public Task<KfzbFeastCardBuyResult> BuyKfzbFeastCardsAsync(int type) => SendAsync<KfzbFeastCardBuyResult>("POST","/api/kfzb/feast/cards/buy",new KfzbFeastCardBuyRequest{type=type},true);
        public Task<KfzbFeastDrinkResult> BuyKfzbFeastDrinkAsync() => SendAsync<KfzbFeastDrinkResult>("POST","/api/kfzb/feast/drink",null,true);
        public Task<KfzbFeastRoomView> JoinKfzbFeastRoomAsync(int rank,int cardType) => SendAsync<KfzbFeastRoomView>("POST","/api/kfzb/feast/rooms",new KfzbFeastJoinRequest{rank=rank,cardType=cardType},true);
        public Task<KfzbFeastRoomView> GetKfzbFeastRoomAsync() => SendAsync<KfzbFeastRoomView>("GET","/api/kfzb/feast/rooms/current",null,true);
        public Task<KfgzView> GetKfgzAsync() => SendAsync<KfgzView>("GET","/api/kfgz",null,true);
        public Task<KfgzView> SignupKfgzAsync() => SendAsync<KfgzView>("POST","/api/kfgz/signup",null,true);
        public Task<KfgzWarView> GetKfgzWorldAsync() => SendAsync<KfgzWarView>("GET","/api/kfgz/world",null,true);
        public Task<KfgzWarView> MoveKfgzGeneralAsync(int generalId,int cityId) => SendAsync<KfgzWarView>("POST","/api/kfgz/world/move",new KfgzMoveRequest{generalId=generalId,cityId=cityId},true);
        public Task<KfgzWarView> RetreatKfgzGeneralsAsync(int[] generalIds,int cityId) => SendAsync<KfgzWarView>("POST","/api/kfgz/world/retreat",new KfgzRetreatRequest{generalIds=generalIds,cityId=cityId},true);
        public Task<KfgzRanking> GetKfgzRankingAsync() => SendAsync<KfgzRanking>("GET","/api/kfgz/ranking",null,true);
        public Task<PlayerView> ChooseForceAsync(int forceId) => SendAsync<PlayerView>("POST", "/api/player/force", new ForceRequest { forceId = forceId }, true);
        public Task<MainCityResponse> GetMainCityAsync() => SendAsync<MainCityResponse>("GET", "/api/main-city", null, true);
        public Task<UpgradeResponse> UpgradeAsync(int buildingId) => SendAsync<UpgradeResponse>("POST", "/api/buildings/" + buildingId + "/upgrade", null, true);
        public Task<RandomNamesResponse> RandomNamesAsync(bool male, int count = 5) => SendAsync<RandomNamesResponse>("GET", "/api/player/random-names?male=" + (male ? "true" : "false") + "&count=" + count, null, true);
        public Task<PlayerView> SetNameAsync(string name, int pic) => SendAsync<PlayerView>("POST", "/api/player/name", new SetNameRequest { name = name, pic = pic }, true);
        public Task<GeneralRosterResponse> GetGeneralsAsync() => SendAsync<GeneralRosterResponse>("GET", "/api/generals", null, true);
        public Task<TavernResponse> GetTavernAsync(int type) => SendAsync<TavernResponse>("GET", "/api/tavern?type=" + type, null, true);
        public Task<TavernResponse> RefreshTavernAsync(int type) => SendAsync<TavernResponse>("POST", "/api/tavern/" + type + "/refresh", null, true);
        public Task<TavernResponse> LockGeneralAsync(int generalId, bool locked) => SendAsync<TavernResponse>("POST", "/api/tavern/generals/" + generalId + (locked ? "/lock" : "/unlock"), null, true);
        public Task<RecruitGeneralResponse> RecruitGeneralAsync(int generalId) => SendAsync<RecruitGeneralResponse>("POST", "/api/tavern/generals/" + generalId + "/recruit", null, true);
        public Task<StoreResponse> GetEquipmentStoreAsync(int type) => SendAsync<StoreResponse>("GET", "/api/equipment/store?type=" + type, null, true);
        public Task<StoreResponse> RefreshEquipmentStoreAsync(int type) => SendAsync<StoreResponse>("POST", "/api/equipment/store/" + type + "/refresh", null, true);
        public Task<StoreResponse> LockEquipmentOfferAsync(int equipmentId, bool locked) => SendAsync<StoreResponse>("POST", "/api/equipment/store/items/" + equipmentId + (locked ? "/lock" : "/unlock"), null, true);
        public Task<BuyEquipmentResponse> BuyEquipmentAsync(int equipmentId) => SendAsync<BuyEquipmentResponse>("POST", "/api/equipment/store/items/" + equipmentId + "/buy", null, true);
        public Task<InventoryResponse> GetEquipmentInventoryAsync() => SendAsync<InventoryResponse>("GET", "/api/equipment/inventory", null, true);
        public Task<EquipEquipmentResponse> EquipEquipmentAsync(long instanceId, int generalId) => SendAsync<EquipEquipmentResponse>("POST", "/api/equipment/inventory/" + instanceId + "/equip", new EquipRequest { generalId = generalId }, true);
        public Task<PlayerEquipmentView> UnequipEquipmentAsync(long instanceId) => SendAsync<PlayerEquipmentView>("POST", "/api/equipment/inventory/" + instanceId + "/unequip", null, true);
        public Task<SellEquipmentResponse> SellEquipmentAsync(long instanceId) => SendAsync<SellEquipmentResponse>("POST", "/api/equipment/inventory/" + instanceId + "/sell", null, true);
        public Task<TechnologyListResponse> GetTechnologyAsync(int page = 1) => SendAsync<TechnologyListResponse>("GET", "/api/technology?page=" + Math.Max(1, page), null, true);
        public Task<TechnologyInjectResponse> InjectTechnologyAsync(int technologyId) => SendAsync<TechnologyInjectResponse>("POST", "/api/technology/" + technologyId + "/inject", null, true);
        public Task<TechnologyResearchResponse> ResearchTechnologyAsync(int technologyId) => SendAsync<TechnologyResearchResponse>("POST", "/api/technology/" + technologyId + "/research", null, true);
        public Task<WorldResponse> GetWorldAsync() => SendAsync<WorldResponse>("GET", "/api/world", null, true);
        public Task<WorldResponse> MoveWorldGeneralAsync(int generalId, int cityId) =>
            SendAsync<WorldResponse>("POST", "/api/world/generals/" + generalId + "/move", new WorldMoveRequest { cityId = cityId }, true);
        public Task<WorldResponse> AutoMoveWorldGeneralAsync(int generalId, int cityId) =>
            SendAsync<WorldResponse>("POST", "/api/world/generals/" + generalId + "/auto-move", new WorldMoveRequest { cityId = cityId }, true);
        public Task<WorldCityDetailResponse> GetWorldCityAsync(int cityId) =>
            SendAsync<WorldCityDetailResponse>("GET", "/api/world/cities/" + cityId, null, true);
        public Task<BattleView> GetBattleAsync(long battleId) => SendAsync<BattleView>("GET", "/api/battles/" + battleId, null, true);
        public Task<BattleView> AdvanceBattleAsync(long battleId) => SendAsync<BattleView>("POST", "/api/battles/" + battleId + "/advance", null, true);
        public Task<BattleView> ChooseBattleActionAsync(long battleId,int generalId,int actionType,int strategyId) => SendAsync<BattleView>("POST", "/api/battles/" + battleId + "/action", new BattleActionRequest{generalId=generalId,actionType=actionType,strategyId=strategyId}, true);
        public Task<NationView> GetNationAsync() => SendAsync<NationView>("GET", "/api/nation", null, true);
        public Task<NationSalaryResponse> ClaimNationSalaryAsync() => SendAsync<NationSalaryResponse>("POST", "/api/nation/salary", null, true);
        public Task<PoliticsView> GetPoliticsAsync() => SendAsync<PoliticsView>("GET", "/api/nation/politics", null, true);
        public Task<PoliticsReward> ChoosePoliticsAsync(int buildingId,int option) => SendAsync<PoliticsReward>("POST", "/api/nation/politics/choose", new PoliticsChoiceRequest{buildingId=buildingId,option=option}, true);
        public Task<CivilAffairsView> GetCivilAffairsAsync(int generalId) => SendAsync<CivilAffairsView>("GET", "/api/nation/affairs/"+generalId, null, true);
        public Task StartCivilAffairAsync(int generalId,int affairId) => SendAsync<object>("POST", "/api/nation/affairs/start", new CivilAffairRequest{generalId=generalId,affairId=affairId}, true);
        public Task<PoliticsReward> StopCivilAffairAsync(int generalId,int affairId) => SendAsync<PoliticsReward>("POST", "/api/nation/affairs/stop", new CivilAffairRequest{generalId=generalId,affairId=affairId}, true);
        public Task<OfficeView[]> GetOfficesAsync() => SendAsync<OfficeView[]>("GET", "/api/nation/offices", null, true);
        public Task ApplyOfficeAsync(int buildingId) => SendAsync<object>("POST", "/api/nation/offices/apply", new OfficeApplyRequest{buildingId=buildingId}, true);
        public Task AcceptOfficeAsync(long playerId) => SendAsync<object>("POST", "/api/nation/offices/accept", new OfficeMemberRequest{playerId=playerId}, true);
        public Task RefuseOfficeAsync(long playerId) => SendAsync<object>("POST", "/api/nation/offices/refuse", new OfficeMemberRequest{playerId=playerId}, true);
        public Task KickOfficeAsync(long playerId) => SendAsync<object>("POST", "/api/nation/offices/kick", new OfficeMemberRequest{playerId=playerId}, true);
        public Task SetOfficeAutoPassAsync(bool enabled) => SendAsync<object>("POST", "/api/nation/offices/auto-pass", new OfficeAutoPassRequest{enabled=enabled}, true);
        public Task QuitOfficeAsync() => SendAsync<object>("POST", "/api/nation/offices/quit", null, true);
        public Task<PositionBattleResponse> StartPositionBattleAsync(int buildingId,int generalId) => SendAsync<PositionBattleResponse>("POST", "/api/nation/offices/battle", new PositionBattleRequest{buildingId=buildingId,generalId=generalId}, true);
        public Task<NationTrialView> GetNationTrialAsync() => SendAsync<NationTrialView>("GET","/api/nation/trial",null,true);
        public Task<NationTrialView> StartNationTrialAsync() => SendAsync<NationTrialView>("POST","/api/nation/trial/start",null,true);
        public Task<PositionBattleResponse> StartNationTrialBattleAsync(int cityId,int generalId) => SendAsync<PositionBattleResponse>("POST","/api/nation/trial/battle",new NationTrialBattleRequest{cityId=cityId,generalId=generalId},true);
        public Task<NationTrialReward> ClaimNationTrialRewardAsync() => SendAsync<NationTrialReward>("POST","/api/nation/trial/reward",null,true);
        public Task<NationRankEntry[]> GetNationRankAsync(string kind) => SendAsync<NationRankEntry[]>("GET","/api/nation/rank/"+kind,null,true);
        public Task<NationTaskView> GetNationTaskAsync() => SendAsync<NationTaskView>("GET","/api/nation/task",null,true);
        public Task<NationTaskView> StartNationUpgradeAsync() => SendAsync<NationTaskView>("POST","/api/nation/task/upgrade/start",null,true);
        public Task<PositionBattleResponse> StartNationTaskBattleAsync(int cityId,int generalId) => SendAsync<PositionBattleResponse>("POST","/api/nation/task/battle",new NationTrialBattleRequest{cityId=cityId,generalId=generalId},true);
        public Task<NationInvestmentReward> InvestNationAsync() => SendAsync<NationInvestmentReward>("POST","/api/nation/invest",null,true);
        public Task<NationProtectionView> GetNationProtectionAsync() => SendAsync<NationProtectionView>("GET","/api/nation/protection",null,true);
        public Task<NationTrialReward> ClaimNationProtectionRewardAsync() => SendAsync<NationTrialReward>("POST","/api/nation/protection/reward",null,true);
        public Task<MailPage> GetMailAsync(int page=0,bool deleted=false) => SendAsync<MailPage>("GET","/api/mail?page="+page+"&deleted="+(deleted?"true":"false"),null,true);
        public Task ReadMailAsync(long id) => SendAsync<object>("POST","/api/mail/"+id+"/read",null,true);
        public Task<MailClaimResponse> ClaimMailAsync(long id) => SendAsync<MailClaimResponse>("POST","/api/mail/"+id+"/claim",null,true);
        public Task DeleteMailAsync(long id) => SendAsync<object>("DELETE","/api/mail/"+id,null,true);
        public Task RetrieveMailAsync(long id) => SendAsync<object>("POST","/api/mail/"+id+"/retrieve",null,true);
        public Task<MarketView> GetMarketAsync() => SendAsync<MarketView>("GET","/api/market",null,true);
        public Task<MarketBuyResult> BuyMarketAsync(int slot,string key) => SendAsync<MarketBuyResult>("POST","/api/market/buy",new MarketBuyRequest{slot=slot,requestKey=key},true);
        public Task<BlackMarketView> GetBlackMarketAsync() => SendAsync<BlackMarketView>("GET","/api/market/black",null,true);
        public Task<BlackMarketTradeResult> TradeBlackMarketAsync(int left,int right,string key) => SendAsync<BlackMarketTradeResult>("POST","/api/market/black/trade",new BlackMarketTradeRequest{left=left,right=right,requestKey=key},true);
        public Task<ChatHistoryResponse> GetChatAsync(string type) => SendAsync<ChatHistoryResponse>("GET","/api/chat/"+type,null,true);
        public Task<ChatMessageView> SendChatAsync(string type,string to,string message) => SendAsync<ChatMessageView>("POST","/api/chat",new ChatSendRequest{type=type,to=to,message=message},true);
        public Task<BlacklistResponse> GetBlacklistAsync() => SendAsync<BlacklistResponse>("GET","/api/chat/blacklist",null,true);
        public Task AddBlacklistAsync(string name) => SendAsync<object>("POST","/api/chat/blacklist/"+UnityWebRequest.EscapeURL(name),null,true);
        public Task RemoveBlacklistAsync(long id) => SendAsync<object>("DELETE","/api/chat/blacklist/"+id,null,true);
        public Task<OnlineGiftView> GetOnlineGiftAsync() => SendAsync<OnlineGiftView>("GET","/api/activities/online-gift",null,true);
        public Task<OnlineGiftReward> ClaimOnlineGiftAsync(string key) => SendAsync<OnlineGiftReward>("POST","/api/activities/online-gift/claim",new OnlineGiftClaimRequest{requestKey=key},true);
        public Task<DailyGiftView> GetDailyGiftAsync()=>SendAsync<DailyGiftView>("GET","/api/activities/daily-gift",null,true);
        public Task<DailyGiftReward> ClaimDailyGiftAsync(string key)=>SendAsync<DailyGiftReward>("POST","/api/activities/daily-gift/claim",new DailyGiftRequest{requestKey=key},true);
        public Task<BattleExpActivityView> GetBattleExpActivityAsync()=>SendAsync<BattleExpActivityView>("GET","/api/activities/battle-exp",null,true);
        public Task<BattleExpActivityView> ActivateBattleExpActivityAsync()=>SendAsync<BattleExpActivityView>("POST","/api/activities/battle-exp/activate",null,true);
        public Task<LevelExpActivityView> GetLevelExpActivityAsync()=>SendAsync<LevelExpActivityView>("GET","/api/activities/level-exp",null,true);
        public Task<LevelExpActivityReward> ClaimLevelExpActivityAsync()=>SendAsync<LevelExpActivityReward>("POST","/api/activities/level-exp/claim",null,true);
        public Task<DragonActivityView> GetDragonActivityAsync()=>SendAsync<DragonActivityView>("GET","/api/activities/dragon",null,true);
        public Task<DragonReward> UseDragonAsync(string key)=>SendAsync<DragonReward>("POST","/api/activities/dragon/use",new DragonUseRequest{requestKey=key},true);
        public Task<IronActivityView> GetIronActivityAsync()=>SendAsync<IronActivityView>("GET","/api/activities/iron",null,true);
        public Task<IronClaimResult> ClaimIronActivityAsync(string key)=>SendAsync<IronClaimResult>("POST","/api/activities/iron/claim",new IronClaimRequest{requestKey=key},true);
        public Task<DstqActivityView> GetDstqActivityAsync()=>SendAsync<DstqActivityView>("GET","/api/activities/dstq",null,true);
        public Task<VipView> GetVipAsync()=>SendAsync<VipView>("GET","/api/vip",null,true);
        public Task<VipView> ClaimVipTeamTimesAsync()=>SendAsync<VipView>("POST","/api/vip/7/1/claim",null,true);
        public Task<VipClaimResult> ClaimVipBenefitAsync(int vip,int sequence)=>SendAsync<VipClaimResult>("POST","/api/vip/"+vip+"/"+sequence+"/claim",null,true);
        public Task<TeamListView> GetTeamsAsync()=>SendAsync<TeamListView>("GET","/api/teams",null,true);
        public Task<TeamView> CreateTeamAsync()=>SendAsync<TeamView>("POST","/api/teams",new TeamCreateRequest(),true);
        public Task<TeamView> JoinTeamAsync(string id,int[] generals)=>SendAsync<TeamView>("POST","/api/teams/join",new TeamJoinRequest{teamId=id,generalIds=generals},true);
        public Task<TeamCostView> GetTeamCostAsync(string id)=>SendAsync<TeamCostView>("GET","/api/teams/"+id+"/battle-cost",null,true);
        public Task<TeamCostView> InspireTeamAsync(string id)=>SendAsync<TeamCostView>("POST","/api/teams/"+id+"/inspire",null,true);
        public Task<TeamCostView> OrderTeamAsync(string id)=>SendAsync<TeamCostView>("POST","/api/teams/"+id+"/order",null,true);
        public Task<TeamDeployResult> DeployTeamAsync(string id,long battleId,int count,int type)=>SendAsync<TeamDeployResult>("POST","/api/teams/deploy",new TeamDeployRequest{teamId=id,battleId=battleId,curNum=count,teamBattleType=type},true);

        async Task<T> SendAsync<T>(string method, string path, object body, bool authenticated)
        {
            using var req = new UnityWebRequest(BaseUrl.TrimEnd('/') + path, method);
            req.downloadHandler = new DownloadHandlerBuffer();
            if (body != null)
            {
                var json = JsonUtility.ToJson(body);
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            if (authenticated && !string.IsNullOrEmpty(Token))
                req.SetRequestHeader("Authorization", "Bearer " + Token);

            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                var error = TryParse<ApiError>(req.downloadHandler.text);
                throw new ApiException(error != null && !string.IsNullOrEmpty(error.code) ? error.code : "NETWORK",
                    error != null && !string.IsNullOrEmpty(error.message) ? error.message : req.error);
            }
            return JsonUtility.FromJson<T>(req.downloadHandler.text);
        }

        static T TryParse<T>(string json) where T : class
        {
            try { return string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<T>(json); }
            catch { return null; }
        }
    }
}

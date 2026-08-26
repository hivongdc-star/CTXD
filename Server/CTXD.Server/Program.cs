using CTXD.Server.Data;
using CTXD.Server.Models;
using CTXD.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<GameDb>();
builder.Services.AddSingleton<CanonicalContent>();
builder.Services.AddSingleton<LegacyFormulaService>();
builder.Services.AddSingleton<ExperienceService>();
builder.Services.AddSingleton<TechnologyEffectService>();
builder.Services.AddSingleton<TutorialService>();
builder.Services.AddSingleton<PlayerQueryService>();
builder.Services.AddSingleton<LegacyNameService>();
builder.Services.AddSingleton<ResourceProductionService>();
builder.Services.AddSingleton<BuildingService>();
builder.Services.AddSingleton<GeneralService>();
builder.Services.AddSingleton<TavernService>();
builder.Services.AddSingleton<EquipmentStoreService>();
builder.Services.AddSingleton<EquipmentInventoryService>();
builder.Services.AddSingleton<TechnologyService>();
builder.Services.AddSingleton<WorldService>();
builder.Services.AddSingleton<BattleService>();
builder.Services.AddSingleton<NationService>();
builder.Services.AddSingleton<QuestService>();
builder.Services.AddSingleton<MailService>();
builder.Services.AddSingleton<MarketService>();
builder.Services.AddSingleton<ChatService>();
builder.Services.AddSingleton<TeamService>();
builder.Services.AddSingleton<TeamTimesGrantService>();
builder.Services.AddSingleton<VipService>();
builder.Services.AddSingleton<PayEntitlementService>();
builder.Services.AddSingleton<KfwdService>();
builder.Services.AddSingleton<KfzbService>();
builder.Services.AddSingleton<KfzbFeastService>();
builder.Services.AddSingleton<KfgzService>();
builder.Services.AddSingleton<KfgzExtendedCombatService>();
builder.Services.AddSingleton<KfgzRushService>();
builder.Services.AddSingleton<ActivityGiftService>();
builder.Services.AddSingleton<DailyGiftService>();
builder.Services.AddSingleton<BattleExpActivityService>();
builder.Services.AddSingleton<LevelExpActivityService>();
builder.Services.AddSingleton<ActivityScheduleService>();
builder.Services.AddSingleton<DragonActivityService>();
builder.Services.AddSingleton<DstqActivityService>();
builder.Services.AddSingleton<IronActivityService>();
builder.Services.AddSingleton<MineService>();
builder.Services.AddSingleton<TreasureService>();
builder.Services.AddSingleton<ISystemMailSender>(sp=>sp.GetRequiredService<MailService>());
builder.Services.AddSingleton<NationProgressService>();
builder.Services.AddSingleton<IPlayerItemInventory,PlayerItemInventoryService>();
builder.Services.AddSingleton<CivilAffairService>();
builder.Services.AddSingleton<PoliticsService>();
builder.Services.AddSingleton<OfficeService>();
builder.Services.AddSingleton<MainCityService>();
builder.Services.AddSingleton<PlayerFlowService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<GamePushHub>();
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddHostedService<BuildingCompletionWorker>();
builder.Services.AddHostedService<ActivityScheduleWorker>();
builder.Services.AddHostedService<TechnologyCompletionWorker>();
builder.Services.AddHostedService<WorldMovementWorker>();
builder.Services.AddHostedService<NationTaskScheduler>();
builder.Services.AddHostedService<WorldScheduledEventService>();
builder.Services.AddHostedService<KfwdWorker>();
builder.Services.AddHostedService<KfzbWorker>();

var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(25) });

app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (GameException ex)
    {
        if (ctx.Response.HasStarted) throw;
        ctx.Response.StatusCode = ex.Status;
        await ctx.Response.WriteAsJsonAsync(new ApiError(ex.Code, ex.Message));
    }
});

await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync(app.Lifetime.ApplicationStopping);

static string? Bearer(HttpRequest request)
{
    var h = request.Headers["Authorization"].ToString();
    return h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? h[7..].Trim() : null;
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", game = "CTXD Remake" }));
app.MapKfgzExtendedCombat();

app.MapGet("/api/quests/current",async(HttpRequest request,AuthService auth,QuestService quests,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await quests.GetCurrentAsync(id,ct));});
app.MapPost("/api/quests/current/claim",async(HttpRequest request,AuthService auth,QuestService quests,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var result=await quests.ClaimCurrentAsync(id,ct);await push.SendAsync(id,"quest.updated",result,ct);return Results.Ok(result);});
app.MapPost("/api/quests/kidnappers/{kidnapperId:int}/defeat",async(int kidnapperId,HttpRequest request,AuthService auth,QuestService quests,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var result=await quests.KillKidnapperAsync(id,kidnapperId,ct);await push.SendAsync(id,"quest.updated",result,ct);return Results.Ok(result);});
app.MapGet("/api/mail",async(int? page,bool? deleted,int? type,HttpRequest request,AuthService auth,MailService mail,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await mail.ListAsync(id,page??0,deleted??false,type,ct));});
app.MapPost("/api/mail/{mailId:long}/read",async(long mailId,HttpRequest request,AuthService auth,MailService mail,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);await mail.ReadAsync(id,mailId,ct);await push.SendAsync(id,"mail.updated",new{mailId,read=true},ct);return Results.Ok();});
app.MapPost("/api/mail/{mailId:long}/claim",async(long mailId,HttpRequest request,AuthService auth,MailService mail,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var rewards=await mail.ClaimAsync(id,mailId,ct);await push.SendAsync(id,"mail.updated",new{mailId,claimed=true},ct);return Results.Ok(new{items=rewards});});
app.MapDelete("/api/mail/{mailId:long}",async(long mailId,HttpRequest request,AuthService auth,MailService mail,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);await mail.DeleteAsync(id,mailId,ct);await push.SendAsync(id,"mail.updated",new{mailId,deleted=true},ct);return Results.Ok();});
app.MapPost("/api/mail/{mailId:long}/retrieve",async(long mailId,HttpRequest request,AuthService auth,MailService mail,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);await mail.RetrieveAsync(id,mailId,ct);await push.SendAsync(id,"mail.updated",new{mailId,deleted=false},ct);return Results.Ok();});
app.MapPost("/api/mail/{mailId:long}/save",async(long mailId,HttpRequest request,AuthService auth,MailService mail,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);await mail.SaveAsync(id,mailId,ct);await push.SendAsync(id,"mail.updated",new{mailId,saved=true},ct);return Results.Ok();});
app.MapDelete("/api/mail/deleted",async(HttpRequest request,AuthService auth,MailService mail,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);await mail.ThoroughDeleteAsync(id,ct);await push.SendAsync(id,"mail.updated",new{deletedCleared=true},ct);return Results.Ok();});
app.MapPost("/api/mail",async(PlayerMailRequest body,HttpRequest request,AuthService auth,MailService mail,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var mailId=await mail.WriteAsync(id,body.Recipient,body.Title,body.Body,ct);return Results.Ok(new{mailId});});
app.MapGet("/api/market",async(HttpRequest request,AuthService auth,MarketService market,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await market.GetAsync(id,ct));});
app.MapPost("/api/market/buy",async(MarketBuyRequest body,HttpRequest request,AuthService auth,MarketService market,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var result=await market.BuyAsync(id,body.Slot,body.RequestKey,ct);await push.SendAsync(id,"market.updated",result,ct);return Results.Ok(result);});
app.MapGet("/api/market/black",async(HttpRequest request,AuthService auth,MarketService market,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await market.GetBlackAsync(id,ct));});
app.MapPost("/api/market/black/trade",async(BlackMarketTradeRequest body,HttpRequest request,AuthService auth,MarketService market,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var result=await market.TradeBlackAsync(id,body.Left,body.Right,body.RequestKey,ct);await push.SendAsync(id,"market.updated",result,ct);return Results.Ok(result);});
app.MapPost("/api/market/black/recover",async(BlackMarketRecoverRequest body,HttpRequest request,AuthService auth,MarketService market,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var result=await market.RecoverBlackAsync(id,body.RequestKey,ct);await push.SendAsync(id,"market.updated",result,ct);return Results.Ok(result);});
app.MapGet("/api/chat/{type}",async(string type,HttpRequest request,AuthService auth,ChatService chat,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(new{items=await chat.HistoryAsync(id,type,ct)});});
app.MapPost("/api/chat",async(ChatSendRequest body,HttpRequest request,AuthService auth,ChatService chat,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await chat.SendAsync(id,body.Type,body.To,body.Message,ct));});
app.MapGet("/api/chat/blacklist",async(HttpRequest request,AuthService auth,ChatService chat,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(new{items=await chat.BlacklistAsync(id,ct)});});
app.MapPost("/api/chat/blacklist/{name}",async(string name,HttpRequest request,AuthService auth,ChatService chat,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);await chat.AddBlockAsync(id,name,ct);return Results.Ok();});
app.MapDelete("/api/chat/blacklist/{entryId:long}",async(long entryId,HttpRequest request,AuthService auth,ChatService chat,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);await chat.RemoveBlockAsync(id,entryId,ct);return Results.Ok();});
app.MapGet("/api/teams",async(HttpRequest request,AuthService auth,TeamService teams,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(new{items=await teams.ListAsync(id,ct)});});
app.MapPost("/api/teams",async(TeamCreateRequest body,HttpRequest request,AuthService auth,TeamService teams,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await teams.CreateAsync(id,body.TeamType,ct));});
app.MapPost("/api/teams/join",async(TeamJoinRequest body,HttpRequest request,AuthService auth,TeamService teams,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await teams.JoinAsync(id,body.TeamId,body.GeneralIds,ct));});
app.MapDelete("/api/teams/{teamId:guid}/generals/{generalId:int}",async(Guid teamId,int generalId,HttpRequest request,AuthService auth,TeamService teams,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);await teams.LeaveAsync(id,teamId,generalId,ct);return Results.Ok();});
app.MapDelete("/api/teams/{teamId:guid}/members/{playerId:long}/generals/{generalId:int}",async(Guid teamId,long playerId,int generalId,HttpRequest request,AuthService auth,TeamService teams,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);await teams.KickAsync(id,teamId,playerId,generalId,ct);return Results.Ok();});
app.MapDelete("/api/teams/{teamId:guid}",async(Guid teamId,HttpRequest request,AuthService auth,TeamService teams,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);await teams.DismissAsync(id,teamId,ct);return Results.Ok();});
app.MapGet("/api/teams/{teamId:guid}/battle-cost",async(Guid teamId,HttpRequest request,AuthService auth,TeamService teams,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await teams.CostAsync(id,teamId,ct));});
app.MapPost("/api/teams/{teamId:guid}/inspire",async(Guid teamId,HttpRequest request,AuthService auth,TeamService teams,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await teams.InspireAsync(id,teamId,ct));});
app.MapPost("/api/teams/{teamId:guid}/order",async(Guid teamId,HttpRequest request,AuthService auth,TeamService teams,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await teams.OrderAsync(id,teamId,ct));});
app.MapPost("/api/teams/deploy",async(TeamDeployRequest body,HttpRequest request,AuthService auth,TeamService teams,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await teams.DeployAsync(id,body,ct));});
app.MapGet("/api/activities/online-gift",async(HttpRequest request,AuthService auth,ActivityGiftService gifts,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await gifts.GetAsync(id,ct));});
app.MapPost("/api/activities/online-gift/claim",async(OnlineGiftClaimRequest body,HttpRequest request,AuthService auth,ActivityGiftService gifts,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await gifts.ClaimAsync(id,body.RequestKey,ct));});
app.MapGet("/api/activities/daily-gift",async(HttpRequest request,AuthService auth,DailyGiftService gifts,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await gifts.GetAsync(id,ct));});
app.MapPost("/api/activities/daily-gift/claim",async(DailyGiftRequest body,HttpRequest request,AuthService auth,DailyGiftService gifts,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await gifts.ClaimAsync(id,body.RequestKey,ct));});
app.MapGet("/api/activities/battle-exp",async(HttpRequest request,AuthService auth,BattleExpActivityService activity,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await activity.GetAsync(id,ct));});
app.MapPost("/api/activities/battle-exp/activate",async(HttpRequest request,AuthService auth,BattleExpActivityService activity,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await activity.ActivateAsync(id,ct));});
app.MapGet("/api/activities/level-exp",async(HttpRequest request,AuthService auth,LevelExpActivityService activity,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await activity.GetAsync(id,ct));});
app.MapPost("/api/activities/level-exp/claim",async(HttpRequest request,AuthService auth,LevelExpActivityService activity,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await activity.ClaimAsync(id,ct));});
app.MapGet("/api/activities/dragon",async(HttpRequest request,AuthService auth,DragonActivityService activity,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await activity.GetAsync(id,ct));});
app.MapPost("/api/activities/dragon/use",async(DragonUseRequest body,HttpRequest request,AuthService auth,DragonActivityService activity,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await activity.UseAsync(id,body.RequestKey,ct));});
app.MapGet("/api/activities/dstq",async(HttpRequest request,AuthService auth,DstqActivityService activity,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await activity.GetAsync(id,ct));});
app.MapGet("/api/vip",async(HttpRequest request,AuthService auth,VipService vip,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await vip.GetAsync(id,ct));});
app.MapPost("/api/vip/{vipLevel:int}/{sequence:int}/claim",async(int vipLevel,int sequence,HttpRequest request,AuthService auth,VipService vip,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await vip.ClaimAsync(id,vipLevel,sequence,ct));});
app.MapPost("/internal/pay/entitlements",async(PayEntitlementRequest body,HttpRequest request,IConfiguration config,PayEntitlementService pay,CancellationToken ct)=>{var key=config["Game:BattleResultKey"];if(string.IsNullOrWhiteSpace(key))return Results.StatusCode(503);if(!string.Equals(request.Headers["X-Battle-Key"],key,StringComparison.Ordinal))return Results.Unauthorized();return Results.Ok(await pay.ApplyAsync(body,ct));});
app.MapGet("/api/activities/iron",async(HttpRequest request,AuthService auth,IronActivityService activity,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await activity.GetAsync(id,ct));});
app.MapPost("/api/activities/iron/claim",async(IronClaimRequest body,HttpRequest request,AuthService auth,IronActivityService activity,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await activity.ClaimAsync(id,body.RequestKey,ct));});

app.MapGet("/api/nation", async (HttpRequest request,AuthService auth,NationService nation,CancellationToken ct) =>
{
    var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await nation.GetAsync(id,ct));
});
app.MapPost("/api/nation/salary", async (HttpRequest request,AuthService auth,NationService nation,GamePushHub push,CancellationToken ct) =>
{
    var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var result=await nation.ClaimSalaryAsync(id,ct);await push.SendAsync(id,"nation.updated",new{reason="salary",result.Output},ct);return Results.Ok(result);
});
app.MapGet("/api/nation/trial",async(HttpRequest request,AuthService auth,NationProgressService nation,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await nation.GetTrialAsync(id,ct));});
app.MapPost("/api/nation/trial/start",async(HttpRequest request,AuthService auth,NationProgressService nation,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var result=await nation.StartTrialAsync(id,ct);await push.BroadcastAsync("nation.updated",new{reason="trial.start",result.ForceId,result.TrialId},ct);return Results.Ok(result);});
app.MapPost("/api/nation/trial/battle",async(NationTrialBattleRequest body,HttpRequest request,AuthService auth,NationProgressService nation,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var city=await nation.ResolveBattleCityAsync(id,false,ct);var battleId=await nation.StartTrialBattleAsync(id,city,body.GeneralId,ct);await push.BroadcastAsync("battle.updated",new{reason="trial.battle",battleId,city},ct);return Results.Ok(new{battleId});});
app.MapPost("/api/nation/trial/reward",async(HttpRequest request,AuthService auth,NationProgressService nation,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var result=await nation.ClaimTrialRewardAsync(id,ct);await push.SendAsync(id,"nation.updated",new{reason="trial.reward",result},ct);return Results.Ok(result);});
app.MapGet("/api/nation/rank/{kind}",async(string kind,HttpRequest request,AuthService auth,NationProgressService nation,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await nation.GetRankAsync(id,kind,ct));});
app.MapGet("/api/nation/task",async(HttpRequest request,AuthService auth,NationProgressService nation,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await nation.GetTaskAsync(id,ct));});
app.MapGet("/api/nation/tasks/scheduled",async(HttpRequest request,AuthService auth,NationProgressService nation,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await nation.GetScheduledTasksAsync(id,ct));});
app.MapPost("/api/nation/tasks/scheduled/{slotKey}/reward",async(string slotKey,HttpRequest request,AuthService auth,NationProgressService nation,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var reward=await nation.ClaimScheduledRewardAsync(id,slotKey,ct);await push.SendAsync(id,"nation.updated",new{reason="nation.task.reward",slotKey,reward},ct);return Results.Ok(reward);});
app.MapPost("/api/nation/task/upgrade/start",async(HttpRequest request,AuthService auth,NationProgressService nation,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var result=await nation.StartUpgradeTaskAsync(id,ct);await push.BroadcastAsync("nation.updated",new{reason="upgrade.start",result},ct);return Results.Ok(result);});
app.MapPost("/api/nation/task/battle",async(NationTrialBattleRequest body,HttpRequest request,AuthService auth,NationProgressService nation,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var city=await nation.ResolveBattleCityAsync(id,true,ct);var battleId=await nation.StartTaskBattleAsync(id,city,body.GeneralId,ct);await push.BroadcastAsync("battle.updated",new{reason="nation.task.battle",battleId,city},ct);return Results.Ok(new{battleId});});
app.MapPost("/api/nation/invest",async(HttpRequest request,AuthService auth,NationProgressService nation,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var result=await nation.InvestAsync(id,ct);await push.BroadcastAsync("nation.updated",new{reason="investment",playerId=id,result},ct);return Results.Ok(result);});
app.MapGet("/api/nation/protection",async(HttpRequest request,AuthService auth,NationProgressService nation,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await nation.GetProtectionAsync(id,ct));});
app.MapPost("/api/nation/protection/reward",async(HttpRequest request,AuthService auth,NationProgressService nation,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var result=await nation.ClaimProtectionRewardAsync(id,ct);await push.SendAsync(id,"nation.updated",new{reason="protection.reward",result},ct);return Results.Ok(result);});
app.MapGet("/api/nation/affairs/{generalId:int}",async(int generalId,HttpRequest request,AuthService auth,CivilAffairService affairs,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await affairs.GetAsync(id,generalId,ct));});
app.MapPost("/api/nation/affairs/start",async(CivilAffairRequest body,HttpRequest request,AuthService auth,CivilAffairService affairs,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);await affairs.StartAsync(id,body.GeneralId,body.AffairId,ct);await push.SendAsync(id,"nation.updated",new{reason="affair.start",body.GeneralId,body.AffairId},ct);return Results.Ok();});
app.MapPost("/api/nation/affairs/stop",async(CivilAffairRequest body,HttpRequest request,AuthService auth,CivilAffairService affairs,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var reward=await affairs.StopAsync(id,body.GeneralId,body.AffairId,ct);await push.SendAsync(id,"nation.updated",new{reason="affair.stop",body.GeneralId,body.AffairId,reward},ct);return Results.Ok(reward);});
app.MapGet("/api/nation/politics",async(HttpRequest request,AuthService auth,PoliticsService politics,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await politics.GetAsync(id,ct));});
app.MapPost("/api/nation/politics/choose",async(PoliticsChoiceRequest body,HttpRequest request,AuthService auth,PoliticsService politics,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var reward=await politics.ChooseAsync(id,body.BuildingId,body.Option,ct);await push.SendAsync(id,"nation.updated",new{reason="politics",reward},ct);return Results.Ok(reward);});
app.MapGet("/api/nation/offices",async(HttpRequest request,AuthService auth,OfficeService offices,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await offices.GetAsync(id,ct));});
app.MapPost("/api/nation/offices/apply",async(OfficeApplyRequest body,HttpRequest request,AuthService auth,OfficeService offices,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);await offices.ApplyAsync(id,body.BuildingId,ct);await push.BroadcastAsync("nation.updated",new{reason="office.apply",body.BuildingId},ct);return Results.Ok();});
app.MapPost("/api/nation/offices/accept",async(OfficeMemberRequest body,HttpRequest request,AuthService auth,OfficeService offices,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);await offices.AcceptAsync(id,body.PlayerId,ct);await push.BroadcastAsync("nation.updated",new{reason="office.accept",body.PlayerId},ct);return Results.Ok();});
app.MapPost("/api/nation/offices/refuse",async(OfficeMemberRequest body,HttpRequest request,AuthService auth,OfficeService offices,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);await offices.RefuseAsync(id,body.PlayerId,ct);await push.BroadcastAsync("nation.updated",new{reason="office.refuse",body.PlayerId},ct);return Results.Ok();});
app.MapPost("/api/nation/offices/kick",async(OfficeMemberRequest body,HttpRequest request,AuthService auth,OfficeService offices,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);await offices.KickAsync(id,body.PlayerId,ct);await push.BroadcastAsync("nation.updated",new{reason="office.kick",body.PlayerId},ct);return Results.Ok();});
app.MapPost("/api/nation/offices/auto-pass",async(OfficeAutoPassRequest body,HttpRequest request,AuthService auth,OfficeService offices,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);await offices.SetAutoPassAsync(id,body.Enabled,ct);await push.BroadcastAsync("nation.updated",new{reason="office.autoPass",body.Enabled},ct);return Results.Ok();});
app.MapPost("/api/nation/offices/quit",async(HttpRequest request,AuthService auth,OfficeService offices,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);await offices.QuitAsync(id,ct);await push.BroadcastAsync("nation.updated",new{reason="office.quit",playerId=id},ct);return Results.Ok();});
app.MapPost("/api/nation/offices/battle",async(PositionBattleRequest body,HttpRequest request,AuthService auth,OfficeService offices,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var battleId=await offices.StartPositionBattleAsync(id,body.BuildingId,body.GeneralId,ct);await push.BroadcastAsync("battle.updated",new{battleId,reason="position.start",body.BuildingId},ct);return Results.Ok(new{battleId});});

app.MapPost("/api/auth/register", async (RegisterRequest req, AuthService auth, CancellationToken ct) =>
{
    var r = await auth.RegisterAsync(req.Username, req.Password, ct);
    return Results.Ok(new AuthResponse(r.Token, r.Player));
});

app.MapPost("/api/auth/login", async (LoginRequest req, AuthService auth, CancellationToken ct) =>
{
    var r = await auth.LoginAsync(req.Username, req.Password, ct);
    return Results.Ok(new AuthResponse(r.Token, r.Player));
});

app.MapGet("/api/player", async (HttpRequest request, AuthService auth, PlayerQueryService players, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    return Results.Ok(await players.GetPlayerAsync(id, ct));
});

app.MapPost("/api/player/force", async (
    ForceRequest req, HttpRequest request, AuthService auth, PlayerFlowService flow, MainCityService city, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var player = await flow.ChooseForceAsync(id, req.ForceId, ct);
    var state = await city.GetAsync(id, ct);
    await push.SendAsync(id, "maincity.updated", state, ct);
    return Results.Ok(player);
});

app.MapGet("/api/player/random-names", async (
    bool? male, int? count, HttpRequest request, AuthService auth, PlayerFlowService flow, CancellationToken ct) =>
{
    _ = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    return Results.Ok(new RandomNamesResponse(await flow.RandomNamesAsync(male ?? true, Math.Clamp(count ?? 5, 1, 5), ct)));
});

app.MapPost("/api/player/name", async (
    SetNameRequest req, HttpRequest request, AuthService auth, PlayerFlowService flow, MainCityService city, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var player = await flow.SetNameAndPicAsync(id, req.Name, req.Pic, ct);
    var state = await city.GetAsync(id, ct);
    await push.SendAsync(id, "maincity.updated", state, ct);
    return Results.Ok(player);
});

app.MapGet("/api/main-city", async (
    HttpRequest request, AuthService auth, MainCityService city, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    return Results.Ok(await city.GetAsync(id, ct));
});

app.MapPost("/api/buildings/{buildingId:int}/upgrade", async (
    int buildingId, HttpRequest request, AuthService auth, BuildingService buildings, MainCityService city, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await buildings.UpgradeAsync(id, buildingId, ct);
    var state = await city.GetAsync(id, ct);
    await push.SendAsync(id, "maincity.updated", state, ct);
    return Results.Ok(result);
});

app.MapGet("/api/generals", async (HttpRequest request, AuthService auth, GeneralService generals, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    return Results.Ok(await generals.GetRosterAsync(id, ct));
});

app.MapGet("/api/tavern", async (int type, HttpRequest request, AuthService auth, TavernService tavern, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    return Results.Ok(await tavern.GetAsync(id, type, ct));
});

app.MapPost("/api/tavern/{type:int}/refresh", async (int type, HttpRequest request, AuthService auth, TavernService tavern, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await tavern.RefreshAsync(id, type, ct);
    await push.SendAsync(id, "tavern.updated", result, ct);
    return Results.Ok(result);
});

app.MapPost("/api/tavern/generals/{generalId:int}/lock", async (int generalId, HttpRequest request, AuthService auth, TavernService tavern, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await tavern.SetLockedAsync(id, generalId, true, ct);
    await push.SendAsync(id, "tavern.updated", result, ct);
    return Results.Ok(result);
});

app.MapPost("/api/tavern/generals/{generalId:int}/unlock", async (int generalId, HttpRequest request, AuthService auth, TavernService tavern, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await tavern.SetLockedAsync(id, generalId, false, ct);
    await push.SendAsync(id, "tavern.updated", result, ct);
    return Results.Ok(result);
});

app.MapPost("/api/tavern/generals/{generalId:int}/recruit", async (int generalId, HttpRequest request, AuthService auth, TavernService tavern, GeneralService generals, MainCityService city, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await tavern.RecruitAsync(id, generalId, ct);
    await push.SendAsync(id, "generals.updated", await generals.GetRosterAsync(id, ct), ct);
    await push.SendAsync(id, "maincity.updated", await city.GetAsync(id, ct), ct);
    return Results.Ok(result);
});

app.MapGet("/api/equipment/store", async (int type, HttpRequest request, AuthService auth, EquipmentStoreService store, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    return Results.Ok(await store.GetAsync(id, type, ct));
});

app.MapPost("/api/equipment/store/{type:int}/refresh", async (int type, HttpRequest request, AuthService auth, EquipmentStoreService store, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await store.RefreshAsync(id, type, ct);
    await push.SendAsync(id, "equipment.store.updated", result, ct);
    return Results.Ok(result);
});

app.MapPost("/api/equipment/store/items/{equipmentId:int}/lock", async (int equipmentId, HttpRequest request, AuthService auth, EquipmentStoreService store, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await store.SetLockedAsync(id, equipmentId, true, ct);
    await push.SendAsync(id, "equipment.store.updated", result, ct);
    return Results.Ok(result);
});

app.MapPost("/api/equipment/store/items/{equipmentId:int}/unlock", async (int equipmentId, HttpRequest request, AuthService auth, EquipmentStoreService store, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await store.SetLockedAsync(id, equipmentId, false, ct);
    await push.SendAsync(id, "equipment.store.updated", result, ct);
    return Results.Ok(result);
});

app.MapPost("/api/equipment/store/items/{equipmentId:int}/buy", async (int equipmentId, HttpRequest request, AuthService auth, EquipmentStoreService store, EquipmentInventoryService inventory, MainCityService city, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await store.BuyAsync(id, equipmentId, ct);
    await push.SendAsync(id, "equipment.inventory.updated", await inventory.GetAsync(id, ct), ct);
    await push.SendAsync(id, "maincity.updated", await city.GetAsync(id, ct), ct);
    return Results.Ok(result);
});

app.MapGet("/api/equipment/inventory", async (HttpRequest request, AuthService auth, EquipmentInventoryService inventory, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    return Results.Ok(await inventory.GetAsync(id, ct));
});

app.MapPost("/api/equipment/inventory/{instanceId:long}/equip", async (long instanceId, EquipRequest req, HttpRequest request, AuthService auth, EquipmentInventoryService inventory, GeneralService generals, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await inventory.EquipAsync(id, instanceId, req.GeneralId, ct);
    await push.SendAsync(id, "equipment.inventory.updated", await inventory.GetAsync(id, ct), ct);
    await push.SendAsync(id, "generals.updated", await generals.GetRosterAsync(id, ct), ct);
    return Results.Ok(result);
});

app.MapPost("/api/equipment/inventory/{instanceId:long}/unequip", async (long instanceId, HttpRequest request, AuthService auth, EquipmentInventoryService inventory, GeneralService generals, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await inventory.UnequipAsync(id, instanceId, ct);
    await push.SendAsync(id, "equipment.inventory.updated", await inventory.GetAsync(id, ct), ct);
    await push.SendAsync(id, "generals.updated", await generals.GetRosterAsync(id, ct), ct);
    return Results.Ok(result);
});

app.MapPost("/api/equipment/inventory/{instanceId:long}/sell", async (long instanceId, HttpRequest request, AuthService auth, EquipmentInventoryService inventory, MainCityService city, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await inventory.SellAsync(id, instanceId, ct);
    await push.SendAsync(id, "equipment.inventory.updated", await inventory.GetAsync(id, ct), ct);
    await push.SendAsync(id, "maincity.updated", await city.GetAsync(id, ct), ct);
    return Results.Ok(result);
});

app.MapGet("/api/technology", async (int? page, HttpRequest request, AuthService auth, TechnologyService technology, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    return Results.Ok(await technology.GetAsync(id, Math.Max(1, page ?? 1), ct));
});

app.MapPost("/api/technology/{technologyId:int}/inject", async (int technologyId, HttpRequest request, AuthService auth, TechnologyService technology, MainCityService city, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await technology.InjectAsync(id, technologyId, ct);
    await push.SendAsync(id, "technology.updated", await technology.GetAsync(id, 1, ct), ct);
    await push.SendAsync(id, "maincity.updated", await city.GetAsync(id, ct), ct);
    return Results.Ok(result);
});

app.MapPost("/api/technology/{technologyId:int}/research", async (int technologyId, HttpRequest request, AuthService auth, TechnologyService technology, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await technology.ResearchAsync(id, technologyId, ct);
    await push.SendAsync(id, "technology.updated", await technology.GetAsync(id, 1, ct), ct);
    return Results.Ok(result);
});

app.MapGet("/api/world", async (HttpRequest request, AuthService auth, WorldService world, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    return Results.Ok(await world.GetAsync(id, ct));
});

app.MapPost("/api/world/generals/{generalId:int}/move", async (
    int generalId, WorldMoveRequest req, HttpRequest request, AuthService auth, WorldService world, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await world.MoveAsync(id, generalId, req.CityId, ct);
    await push.SendAsync(id, "world.updated", result, ct);
    return Results.Ok(result);
});

app.MapPost("/api/world/generals/{generalId:int}/auto-move", async (
    int generalId, WorldMoveRequest req, HttpRequest request, AuthService auth, WorldService world, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await world.AutoMoveAsync(id, generalId, req.CityId, ct);
    await push.SendAsync(id, "world.updated", result, ct);
    return Results.Ok(result);
});

app.MapPost("/api/world/events/battle",async(WorldEventBattleRequest body,HttpRequest request,AuthService auth,WorldService world,GamePushHub push,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);var battleId=await world.StartScheduledEventBattleAsync(id,body.GeneralId,body.EventNpcId,ct);await push.BroadcastAsync("battle.updated",new{reason="world.event.battle",battleId,body.EventNpcId},ct);return Results.Ok(new{battleId});});
app.MapGet("/api/world/events",async(HttpRequest request,AuthService auth,WorldService world,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await world.GetScheduledEventsAsync(id,ct));});

app.MapGet("/api/world/cities/{cityId:int}", async (
    int cityId, HttpRequest request, AuthService auth, WorldService world, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    return Results.Ok(await world.GetCityDetailAsync(id, cityId, ct));
});

app.MapPost("/internal/world/battles/{battleId:long}/result", async (
    long battleId, WorldBattleResultRequest req, HttpRequest request, IConfiguration config,
    WorldService world, GamePushHub push, CancellationToken ct) =>
{
    var expected = config["Game:BattleResultKey"];
    if (string.IsNullOrWhiteSpace(expected)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    if (!string.Equals(request.Headers["X-Battle-Key"], expected, StringComparison.Ordinal)) return Results.Unauthorized();
    var result = await world.ResolveBattleAsync(battleId, req.AttackerWon, req.ResultPayload, ct);
    await push.BroadcastAsync("world.updated", new { reason = "battle.result", result.BattleId, result.CityId, result.WinnerForceId, result.Conquered }, ct);
    return Results.Ok(result);
});
app.MapGet("/internal/activities",async(HttpRequest request,IConfiguration config,ActivityScheduleService activities,CancellationToken ct)=>{var expected=config["Game:BattleResultKey"];if(string.IsNullOrWhiteSpace(expected))return Results.StatusCode(503);if(!string.Equals(request.Headers["X-Battle-Key"],expected,StringComparison.Ordinal))return Results.Unauthorized();return Results.Ok(new{items=await activities.ListAsync(ct)});});
app.MapPost("/internal/activities",async(ActivityScheduleRequest body,HttpRequest request,IConfiguration config,ActivityScheduleService activities,CancellationToken ct)=>{var expected=config["Game:BattleResultKey"];if(string.IsNullOrWhiteSpace(expected))return Results.StatusCode(503);if(!string.Equals(request.Headers["X-Battle-Key"],expected,StringComparison.Ordinal))return Results.Unauthorized();return Results.Ok(await activities.ProvisionAsync(body,ct));});
app.MapPost("/internal/kfwd/seasons",async(KfwdSeasonProvision body,HttpRequest request,IConfiguration config,KfwdService kfwd,CancellationToken ct)=>{var expected=config["Game:BattleResultKey"];if(string.IsNullOrWhiteSpace(expected))return Results.StatusCode(503);if(!string.Equals(request.Headers["X-Battle-Key"],expected,StringComparison.Ordinal))return Results.Unauthorized();return Results.Ok(new{seasonId=await kfwd.ProvisionAsync(body,ct)});});
app.MapPost("/internal/kfzb/seasons",async(KfzbSeasonProvision body,HttpRequest request,IConfiguration config,KfzbService kfzb,CancellationToken ct)=>{var expected=config["Game:BattleResultKey"];if(string.IsNullOrWhiteSpace(expected))return Results.StatusCode(503);if(!string.Equals(request.Headers["X-Battle-Key"],expected,StringComparison.Ordinal))return Results.Unauthorized();return Results.Ok(new{seasonId=await kfzb.ProvisionSeasonAsync(body,ct)});});
app.MapPost("/internal/kfzb/matches",async(KfzbMatchProvision body,HttpRequest request,IConfiguration config,KfzbService kfzb,CancellationToken ct)=>{var expected=config["Game:BattleResultKey"];if(string.IsNullOrWhiteSpace(expected))return Results.StatusCode(503);if(!string.Equals(request.Headers["X-Battle-Key"],expected,StringComparison.Ordinal))return Results.Unauthorized();return Results.Ok(new{matchId=await kfzb.ProvisionMatchAsync(body,ct)});});
app.MapPost("/internal/kfzb/feast/organizers",async(KfzbFeastOrganizerProvision body,HttpRequest request,IConfiguration config,KfzbFeastService feast,CancellationToken ct)=>{var expected=config["Game:BattleResultKey"];if(string.IsNullOrWhiteSpace(expected))return Results.StatusCode(503);if(!string.Equals(request.Headers["X-Battle-Key"],expected,StringComparison.Ordinal))return Results.Unauthorized();await feast.ProvisionOrganizerAsync(body,ct);return Results.Ok();});
app.MapPost("/internal/kfgz/seasons",async(KfgzSeasonProvision body,HttpRequest request,IConfiguration config,KfgzService kfgz,CancellationToken ct)=>{var expected=config["Game:BattleResultKey"];if(string.IsNullOrWhiteSpace(expected))return Results.StatusCode(503);if(!string.Equals(request.Headers["X-Battle-Key"],expected,StringComparison.Ordinal))return Results.Unauthorized();return Results.Ok(new{seasonId=await kfgz.ProvisionAsync(body,ct)});});
app.MapPost("/internal/kfgz/rounds",async(KfgzRoundProvision body,HttpRequest request,IConfiguration config,KfgzService kfgz,CancellationToken ct)=>{var expected=config["Game:BattleResultKey"];if(string.IsNullOrWhiteSpace(expected))return Results.StatusCode(503);if(!string.Equals(request.Headers["X-Battle-Key"],expected,StringComparison.Ordinal))return Results.Unauthorized();return Results.Ok(new{roundId=await kfgz.ProvisionRoundAsync(body,ct)});});
app.MapGet("/api/kfgz",async(HttpRequest request,AuthService auth,KfgzService kfgz,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await kfgz.GetAsync(id,ct));});
app.MapPost("/api/kfgz/signup",async(HttpRequest request,AuthService auth,KfgzService kfgz,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await kfgz.SignupAsync(id,ct));});
app.MapGet("/api/kfgz/world",async(HttpRequest request,AuthService auth,KfgzService kfgz,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await kfgz.WorldAsync(id,ct));});
app.MapPost("/api/kfgz/world/move",async(KfgzMoveRequest body,HttpRequest request,AuthService auth,KfgzService kfgz,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await kfgz.MoveAsync(id,body.GeneralId,body.CityId,ct));});
app.MapPost("/api/kfgz/world/retreat",async(KfgzRetreatRequest body,HttpRequest request,AuthService auth,KfgzService kfgz,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await kfgz.RetreatAsync(id,body,ct));});
app.MapGet("/api/kfgz/ranking",async(HttpRequest request,AuthService auth,KfgzService kfgz,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(new{items=await kfgz.RankingAsync(id,ct)});});
app.MapGet("/api/kfzb",async(HttpRequest request,AuthService auth,KfzbService kfzb,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await kfzb.GetAsync(id,ct));});
app.MapGet("/api/kfzb/table",async(HttpRequest request,AuthService auth,KfzbService kfzb,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(new{items=await kfzb.TableAsync(id,ct)});});
app.MapPost("/api/kfzb/signup",async(HttpRequest request,AuthService auth,KfzbService kfzb,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await kfzb.SignupAsync(id,ct));});
app.MapPost("/api/kfzb/sync",async(KfzbSyncRequest body,HttpRequest request,AuthService auth,KfzbService kfzb,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await kfzb.SyncAsync(id,body.GeneralIds,ct));});
app.MapPost("/api/kfzb/support",async(KfzbSupportRequest body,HttpRequest request,AuthService auth,KfzbService kfzb,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await kfzb.SupportAsync(id,body,ct));});
app.MapPost("/api/kfzb/support/claim",async(HttpRequest request,AuthService auth,KfzbService kfzb,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await kfzb.ClaimSupportAsync(id,ct));});
app.MapGet("/api/kfzb/feast/cards",async(HttpRequest request,AuthService auth,KfzbService kfzb,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await kfzb.FeastCardsAsync(id,ct));});
app.MapPost("/api/kfzb/feast/cards/buy",async(KfzbFeastCardBuyRequest body,HttpRequest request,AuthService auth,KfzbService kfzb,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await kfzb.BuyFeastCardsAsync(id,body.Type,ct));});
app.MapPost("/api/kfzb/feast/drink",async(HttpRequest request,AuthService auth,KfzbFeastService feast,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await feast.BuyDrinkAsync(id,ct));});
app.MapPost("/api/kfzb/feast/rooms",async(KfzbFeastJoinRequest body,HttpRequest request,AuthService auth,KfzbFeastService feast,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await feast.JoinAsync(id,body,ct));});
app.MapGet("/api/kfzb/feast/rooms/current",async(HttpRequest request,AuthService auth,KfzbFeastService feast,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await feast.RoomAsync(id,ct));});
app.MapGet("/api/kfwd",async(HttpRequest request,AuthService auth,KfwdService kfwd,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await kfwd.GetAsync(id,ct));});
app.MapGet("/api/kfwd/ranking",async(HttpRequest request,AuthService auth,KfwdService kfwd,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(new{items=await kfwd.RankingAsync(id,ct)});});
app.MapPost("/api/kfwd/signup",async(KfwdSignupRequest body,HttpRequest request,AuthService auth,KfwdService kfwd,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await kfwd.SignupAsync(id,body.GeneralIds,ct));});
app.MapPost("/api/kfwd/sync",async(KfwdSignupRequest body,HttpRequest request,AuthService auth,KfwdService kfwd,CancellationToken ct)=>{var id=await auth.ResolvePlayerIdAsync(Bearer(request),ct);return Results.Ok(await kfwd.SyncAsync(id,body.GeneralIds,ct));});

app.MapGet("/api/battles/{battleId:long}", async (
    long battleId, HttpRequest request, AuthService auth, BattleService battle, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    return Results.Ok(await battle.GetAsync(id, battleId, ct));
});

app.MapPost("/api/battles/{battleId:long}/advance", async (
    long battleId, HttpRequest request, AuthService auth, BattleService battle, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await battle.AdvanceAsync(id, battleId, ct);
    await push.BroadcastAsync("battle.updated", new { battleId, result.Status, result.RoundNo, result.WinnerSide }, ct);
    if (result.Status != 0) await push.BroadcastAsync("world.updated", new { reason = "battle.result", battleId, result.CityId, result.WinnerSide }, ct);
    return Results.Ok(result);
});

app.MapPost("/api/battles/{battleId:long}/join", async (
    long battleId, BattleJoinRequest body, HttpRequest request, AuthService auth, BattleService battle, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await battle.JoinAttackerAsync(id, battleId, body.GeneralId, ct);
    await push.BroadcastAsync("battle.updated", new { battleId, reason="reinforcement", generalId=body.GeneralId }, ct);
    return Results.Ok(result);
});

app.MapPost("/api/battles/{battleId:long}/action", async (
    long battleId, BattleActionRequest body, HttpRequest request, AuthService auth, BattleService battle, GamePushHub push, CancellationToken ct) =>
{
    var id = await auth.ResolvePlayerIdAsync(Bearer(request), ct);
    var result = await battle.ChooseActionAsync(id, battleId, body.GeneralId, body.ActionType, body.StrategyId, ct);
    await push.BroadcastAsync("battle.updated", new { battleId, reason="action", body.GeneralId, body.ActionType, body.StrategyId }, ct);
    return Results.Ok(result);
});

app.Map("/ws", async (HttpContext ctx, AuthService auth, GamePushHub hub) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    var token = ctx.Request.Query["token"].ToString();
    var playerId = await auth.ResolvePlayerIdAsync(token, ctx.RequestAborted);
    var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    await hub.HoldAsync(playerId, socket, ctx.RequestAborted);
});

app.Run();
